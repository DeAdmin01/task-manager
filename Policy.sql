-- ==========================================================
-- 1. НАСТРОЙКА ОКРУЖЕНИЯ И ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ
-- ==========================================================

-- Включение RLS на всех таблицах в схеме public
DO $$
DECLARE
    r record;
BEGIN
    FOR r IN (SELECT tablename FROM pg_tables WHERE schemaname = 'public')
    LOOP
        EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', r.tablename);
    END LOOP;
END $$;

-- Функция проверки прав (SECURITY DEFINER позволяет читать скрытые таблицы role_permissions)
CREATE OR REPLACE FUNCTION public.has_permission(required_permission text)
RETURNS boolean AS $$
DECLARE
    has_perm boolean;
BEGIN
    SELECT EXISTS (
        SELECT 1
        FROM public.profiles p
        JOIN public.role_permissions rp ON p.role_id = rp.role_id
        JOIN public.permissions perm ON rp.permission_id = perm.id
        WHERE p.id = auth.uid() AND perm.codename = required_permission
    ) INTO has_perm;

    RETURN has_perm;
END;
$$ LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = public;

-- Функция проверки прав для указанного пользователя (нужна для проверки будущих исполнителей тикетов)
CREATE OR REPLACE FUNCTION public.user_has_permission(target_uid uuid, required_permission text)
RETURNS boolean AS $$
DECLARE
    has_perm boolean;
BEGIN
    SELECT EXISTS (
        SELECT 1
        FROM public.profiles p
        JOIN public.role_permissions rp ON p.role_id = rp.role_id
        JOIN public.permissions perm ON rp.permission_id = perm.id
        WHERE p.id = target_uid AND perm.codename = required_permission
    ) INTO has_perm;

    RETURN has_perm;
END;
$$ LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = public;

-- Индексы для ускорения
CREATE INDEX IF NOT EXISTS idx_role_permissions_role_id ON public.role_permissions(role_id);
CREATE INDEX IF NOT EXISTS idx_role_permissions_permission_id ON public.role_permissions(permission_id);


-- ==========================================================
-- 2. ЗАЩИТА СТРУКТУРЫ РОЛЕЙ
-- ==========================================================

-- Триггер: Запрет на назначение/удаление права 'settings.manage' через API
CREATE OR REPLACE FUNCTION prevent_critical_permission_change()
RETURNS TRIGGER AS $$
DECLARE
    critical_perm_id INTEGER;
BEGIN
    SELECT id INTO critical_perm_id FROM public.permissions WHERE codename = 'settings.manage';

    IF (TG_OP = 'INSERT' OR TG_OP = 'UPDATE') AND NEW.permission_id = critical_perm_id THEN
        RAISE EXCEPTION 'Security Alert: Выдача права "settings.manage" запрещена через API.';
    END IF;

    IF (TG_OP = 'DELETE' OR TG_OP = 'UPDATE') AND OLD.permission_id = critical_perm_id THEN
        RAISE EXCEPTION 'Security Alert: Отзыв права "settings.manage" запрещен через API.';
    END IF;

    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_protect_critical_permissions ON role_permissions;
CREATE TRIGGER trg_protect_critical_permissions
BEFORE INSERT OR DELETE OR UPDATE ON role_permissions
FOR EACH ROW EXECUTE FUNCTION prevent_critical_permission_change();


-- ==========================================================
-- 3. ЗАЩИТА СТОЛБЦОВ И АВТОМАТИЗАЦИЯ (TRIGGERS)
-- ==========================================================

-- 3.1. ЗАЩИТА ПРОФИЛЯ (PROFILES) - [ОБНОВЛЕНО: ИЕРАРХИЯ + SOFT DELETE + ЗАПРЕТ СМЕНЫ СВОЕЙ РОЛИ]
CREATE OR REPLACE FUNCTION check_profile_update_permissions()
RETURNS TRIGGER AS $$
DECLARE
    target_has_settings_manage BOOLEAN; -- Цель - Супер-Админ
    target_has_user_manage BOOLEAN;     -- Цель - Менеджер
    actor_has_settings_manage BOOLEAN;  -- Я - Супер-Админ
    actor_has_user_delete BOOLEAN;      -- Я - Админ
BEGIN
    -- Сбор информации о том, КТО делает запрос
    actor_has_settings_manage := public.has_permission('settings.manage');
    actor_has_user_delete := public.has_permission('user.delete');

    -- Сбор информации о ЦЕЛИ (кого редактируем - по старой роли)
    SELECT
        EXISTS (SELECT 1 FROM public.role_permissions rp JOIN public.permissions p ON rp.permission_id = p.id WHERE rp.role_id = OLD.role_id AND p.codename = 'settings.manage'),
        EXISTS (SELECT 1 FROM public.role_permissions rp JOIN public.permissions p ON rp.permission_id = p.id WHERE rp.role_id = OLD.role_id AND p.codename = 'user.manage')
    INTO target_has_settings_manage, target_has_user_manage;

    -- ЛОГИКА 1: Soft Delete / Восстановление (изменение is_active)
    IF NEW.is_active IS DISTINCT FROM OLD.is_active THEN
        -- Нельзя отключить Супер-Админа
        IF target_has_settings_manage THEN
            RAISE EXCEPTION 'Access Denied: Нельзя деактивировать главного администратора.';
        END IF;
        -- Менеджер не может отключить Менеджера или Админа (нужен user.delete)
        IF target_has_user_manage AND NOT actor_has_user_delete THEN
             RAISE EXCEPTION 'Access Denied: Менеджеры не могут деактивировать других менеджеров. Требуются права администратора.';
        END IF;
        -- Базовая проверка прав
        IF NOT (public.has_permission('user.manage') OR public.has_permission('user.delete')) THEN
             RAISE EXCEPTION 'Access Denied: У вас нет прав на деактивацию пользователей.';
        END IF;
        -- Нельзя отключить себя
        IF auth.uid() = OLD.id THEN
             RAISE EXCEPTION 'Access Denied: Вы не можете деактивировать свой собственный аккаунт.';
        END IF;
    END IF;

    -- ЛОГИКА 2: Смена Роли
    IF NEW.role_id IS DISTINCT FROM OLD.role_id THEN

        -- === НОВОЕ ПРАВИЛО: ЗАПРЕТ НА ИЗМЕНЕНИЕ СВОЕЙ ЖЕ РОЛИ ===
        IF auth.uid() = OLD.id THEN
            RAISE EXCEPTION 'Access Denied: Вы не можете изменить свою собственную роль. Попросите другого администратора.';
        END IF;
        -- ========================================================

        IF NOT public.has_permission('user.manage') THEN
             RAISE EXCEPTION 'Access Denied: Нет прав на смену ролей.';
        END IF;

        -- Менеджер (без прав удаления) не может назначать руководящие роли
        IF NOT actor_has_user_delete THEN
            IF EXISTS (
                SELECT 1 FROM public.role_permissions rp
                JOIN public.permissions p ON rp.permission_id = p.id
                WHERE rp.role_id = NEW.role_id
                AND p.codename IN ('settings.manage', 'user.delete', 'user.manage')
            ) THEN
                RAISE EXCEPTION 'Access Denied: Вы не можете назначать руководящие роли.';
            END IF;
        END IF;
    END IF;

    -- ЛОГИКА 3: Обычное редактирование (Имя и т.д.)
    -- Если редактируем себя - ок
    IF auth.uid() = OLD.id THEN RETURN NEW; END IF;

    -- Менеджер не может править профиль равного или старшего
    IF public.has_permission('user.manage') AND NOT actor_has_user_delete THEN
        IF target_has_settings_manage OR target_has_user_manage THEN
             RAISE EXCEPTION 'Access Denied: Вы не можете редактировать профиль равного или вышестоящего сотрудника.';
        END IF;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- Пересоздание триггера на всякий случай
DROP TRIGGER IF EXISTS trg_protect_profile_fields ON profiles;
CREATE TRIGGER trg_protect_profile_fields
BEFORE UPDATE ON profiles
FOR EACH ROW EXECUTE FUNCTION check_profile_update_permissions();

-- 3.2. ЗАЩИТА ТИКЕТОВ (TICKETS)
CREATE OR REPLACE FUNCTION public.check_ticket_update_permissions()
RETURNS TRIGGER AS $$
DECLARE
    has_edit_any boolean;
    has_create boolean;
    has_assign boolean;
    has_verify boolean;

    is_creator boolean := OLD.creator_id = auth.uid();
    is_assignee boolean := OLD.assignee_id = auth.uid();

    status_changed boolean := NEW.status_id IS DISTINCT FROM OLD.status_id;
    assignee_changed boolean := NEW.assignee_id IS DISTINCT FROM OLD.assignee_id;
    
    -- Проверка изменения сути заявки (игнорируем системные даты, статусы и исполнителей)
    core_fields_changed boolean := (to_jsonb(NEW) - 'status_id' - 'assignee_id' - 'resolution_note' - 'updated_at' - 'closed_at' - 'verified_at') 
                                   IS DISTINCT FROM 
                                   (to_jsonb(OLD) - 'status_id' - 'assignee_id' - 'resolution_note' - 'updated_at' - 'closed_at' - 'verified_at');
BEGIN
    -- Получаем права пользователя
    has_edit_any := public.has_permission('ticket.edit_any');
    has_create := public.has_permission('ticket.create');
    has_assign := public.has_permission('ticket.assign');
    has_verify := public.has_permission('ticket.verify');

    -- 1. Абсолютное право
    IF has_edit_any THEN
        RETURN NEW;
    END IF;

    -- 2. Смена исполнителя
    IF assignee_changed THEN
        IF NOT has_assign THEN
            RAISE EXCEPTION 'Access Denied: У вас нет прав на переназначение исполнителя.';
        END IF;
    END IF;

    -- 3. Редактирование тела заявки
    IF core_fields_changed THEN
        IF NOT (is_creator AND has_create) THEN
            RAISE EXCEPTION 'Access Denied: Только создатель заявки может редактировать её основное содержимое.';
        END IF;
    END IF;

    -- 4. Изменение статуса
    IF status_changed THEN
        IF has_verify THEN
            NULL; -- Может всё
        ELSIF is_creator AND has_create THEN
            NULL; -- Создатель может всё со своей заявкой
        ELSIF is_assignee THEN
            -- НОВАЯ ЛОГИКА: Исполнитель может менять статус на ЛЮБОЙ, кроме "Закрыта" (ID = 5)
            -- Если в вашей базе статус "Закрыта" имеет другой ID
            IF NEW.status_id = 5 THEN
                RAISE EXCEPTION 'Access Denied: Исполнитель не может самостоятельно закрыть заявку. Переведите её "На проверку".';
            END IF;
        ELSE
            RAISE EXCEPTION 'Access Denied: У вас нет прав на изменение статуса этой заявки.';
        END IF;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- Пересоздание триггера
DROP TRIGGER IF EXISTS trg_protect_ticket_fields ON tickets;
CREATE TRIGGER trg_protect_ticket_fields
BEFORE UPDATE ON tickets
FOR EACH ROW EXECUTE FUNCTION check_ticket_update_permissions();

-- ==========================================================
-- 4. ПОЛИТИКИ ДОСТУПА (RLS) - СПРАВОЧНИКИ
-- ==========================================================

DO $$
DECLARE
    t text;
BEGIN
    -- Стандартные системные справочники (только для settings.manage)
    FOR t IN SELECT table_name FROM information_schema.tables
             WHERE table_schema = 'public'
             AND table_name IN ('ticket_statuses', 'ticket_priorities', 'units', 'sensor_types')
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS "Admin_Full" ON %I', t);
        EXECUTE format('CREATE POLICY "Admin_Full" ON %I FOR ALL TO authenticated USING (public.has_permission(''settings.manage''))', t);
    END LOOP;

    -- Справочники ЗОН (Map page) - доступны и главному админу, и менеджеру зон
    FOR t IN SELECT table_name FROM information_schema.tables
             WHERE table_schema = 'public'
             AND table_name IN ('zone_types', 'stadium_zones')
    LOOP
        EXECUTE format('DROP POLICY IF EXISTS "Admin_Full" ON %I', t);
        -- НОВАЯ ЛОГИКА: Добавлена проверка на zones.manage
        EXECUTE format('CREATE POLICY "Admin_Full" ON %I FOR ALL TO authenticated 
            USING (public.has_permission(''settings.manage'') OR public.has_permission(''zones.manage''))', t);
    END LOOP;
END $$;
-- 4.1 ТАБЛИЦА ROLES (Роли)
DROP POLICY IF EXISTS "Roles_Select_Public" ON roles;
DROP POLICY IF EXISTS "Roles_Manage" ON roles;
CREATE POLICY "Roles_Select_Public" ON roles FOR SELECT TO authenticated USING (true);
CREATE POLICY "Roles_Manage" ON roles FOR ALL TO authenticated USING (public.has_permission('settings.manage'));

-- 4.2 ТАБЛИЦА PERMISSIONS (Разрешения)
DROP POLICY IF EXISTS "Permissions_Select_Public" ON permissions;
DROP POLICY IF EXISTS "Permissions_Manage" ON permissions;
CREATE POLICY "Permissions_Select_Public" ON permissions FOR SELECT TO authenticated USING (true);

-- 4.3 ТАБЛИЦА ROLE_PERMISSIONS (Связи ролей и прав)
DROP POLICY IF EXISTS "RolePerms_Manage" ON role_permissions;
CREATE POLICY "RolePerms_Manage" ON role_permissions FOR ALL TO authenticated
USING (public.has_permission('settings.manage'));


-- ==========================================================
-- 5. ПОЛИТИКИ ДОСТУПА (RLS) - ПРОФИЛИ - [ОБНОВЛЕНО]
-- ==========================================================

DROP POLICY IF EXISTS "Profiles_Select" ON profiles;
DROP POLICY IF EXISTS "Profiles_Insert" ON profiles;
DROP POLICY IF EXISTS "Profiles_Update_Own" ON profiles;
DROP POLICY IF EXISTS "Profiles_Update" ON profiles;
DROP POLICY IF EXISTS "Profiles_Update_Management" ON profiles;
DROP POLICY IF EXISTS "Profiles_Delete" ON profiles;

-- 5.1 ЧТЕНИЕ: Скрываем уволенных (is_active=false), если у смотрящего нет прав user.view_all
CREATE POLICY "Profiles_Select" ON profiles FOR SELECT TO authenticated
USING (
    is_active = true                   -- Активные видны всем
    OR auth.uid() = id                 -- Свой профиль виден всегда
    OR public.has_permission('user.view_all') -- HR/Админ видят всех
);

-- 5.2 СОЗДАНИЕ: Менеджеры и Админы
CREATE POLICY "Profiles_Insert" ON profiles FOR INSERT TO authenticated
WITH CHECK (
    public.has_permission('user.manage') OR public.has_permission('user.delete')
);

-- 5.3 РЕДАКТИРОВАНИЕ: Свой + Менеджерский доступ
-- (Конкретная логика защиты "кто кого" выполняется в Триггере check_profile_update_permissions)
CREATE POLICY "Profiles_Update" ON profiles FOR UPDATE TO authenticated
USING (
    auth.uid() = id
    OR public.has_permission('user.manage')
    OR public.has_permission('user.delete')
);

-- 5.4 УДАЛЕНИЕ: ПОЛНОСТЬЮ ЗАПРЕЩЕНО
-- Физическое удаление данных не допускается. Используйте is_active = false (UPDATE).

-- ==========================================================
-- 6. ПОЛИТИКИ ДОСТУПА (RLS) - ТИКЕТЫ [ОБНОВЛЕНО]
-- ==========================================================

DROP POLICY IF EXISTS "Tickets_Select" ON tickets;
DROP POLICY IF EXISTS "Tickets_Insert" ON tickets;
DROP POLICY IF EXISTS "Tickets_Update" ON tickets;

-- ВИДИМОСТЬ: Кто видит заявку?
CREATE POLICY "Tickets_Select" ON tickets FOR SELECT TO authenticated
USING (
    public.has_permission('ticket.edit_any') OR
    public.has_permission('ticket.view_all') OR
    public.has_permission('ticket.assign') OR  -- assign включает видимость всех
    public.has_permission('ticket.verify') OR  -- verify включает видимость всех
    auth.uid() = creator_id OR                 -- Создатель видит свои
    auth.uid() = assignee_id                   -- Исполнитель видит назначенные на него
);

-- СОЗДАНИЕ: Кто может создать заявку?
CREATE POLICY "Tickets_Insert" ON tickets FOR INSERT TO authenticated
WITH CHECK (
    (public.has_permission('ticket.create') OR public.has_permission('ticket.edit_any')) AND
    auth.uid() = creator_id AND
    -- Проверка, что назначаемый исполнитель имеет право ticket.executor
    (assignee_id IS NULL OR public.user_has_permission(assignee_id, 'ticket.executor'))
);

-- РЕДАКТИРОВАНИЕ: Какие заявки доступны для попытки обновления?
-- (Сама проверка того, ЧТО меняется, происходит в триггере выше)
CREATE POLICY "Tickets_Update" ON tickets FOR UPDATE TO authenticated
USING (
    public.has_permission('ticket.edit_any') OR
    public.has_permission('ticket.assign') OR 
    public.has_permission('ticket.verify') OR 
    (public.has_permission('ticket.create') AND auth.uid() = creator_id) OR
    auth.uid() = assignee_id
)
WITH CHECK (
    -- Не даем сохранить заявку, если попытались назначить исполнителя без права executor
    (assignee_id IS NULL OR public.user_has_permission(assignee_id, 'ticket.executor'))
);



-- ==========================================================
-- 7. ИСТОРИЯ, IOT И ПРОЧЕЕ
-- ==========================================================

-- --- TICKET_HISTORY ---
DROP POLICY IF EXISTS "History_Select" ON ticket_history;
DROP POLICY IF EXISTS "History_Insert" ON ticket_history;

CREATE POLICY "History_Select" ON ticket_history FOR SELECT TO authenticated
USING (
    EXISTS (
        SELECT 1 FROM public.tickets t
        WHERE t.id = ticket_history.ticket_id
        AND (
            public.has_permission('ticket.view_all') OR
            t.assignee_id = auth.uid() OR
            t.creator_id = auth.uid()
        )
    )
);

CREATE POLICY "History_Insert" ON ticket_history FOR INSERT TO authenticated
WITH CHECK (auth.uid() = changed_by);

-- --- SENSOR_DATA (IoT) ---
DROP POLICY IF EXISTS "View_Sensor_Data" ON sensor_data;
DROP POLICY IF EXISTS "Insert_Sensor_Data" ON sensor_data;

CREATE POLICY "View_Sensor_Data" ON sensor_data FOR SELECT TO authenticated USING (true);
CREATE POLICY "Insert_Sensor_Data" ON sensor_data FOR INSERT TO authenticated
WITH CHECK (public.has_permission('iot.manage'));


-- ==========================================================
-- 8. ПОЛИТИКИ ДОСТУПА (RLS) - ЧАТ И УВЕДОМЛЕНИЯ
-- ==========================================================

-- --- NOTIFICATIONS ---
-- 3. Политика ЧТЕНИЯ: Пользователь может читать ТОЛЬКО СВОИ уведомления
DROP POLICY IF EXISTS "Users_Select_Own" ON notifications;
CREATE POLICY "Users_Select_Own" ON notifications
FOR SELECT TO authenticated
USING (auth.uid() = user_id OR auth.uid() = created_by);

-- 4. Политика ОБНОВЛЕНИЯ: Пользователь может менять статус прочтения ТОЛЬКО СВОИХ уведомлений
CREATE POLICY "Users_Update_Own" ON notifications
FOR UPDATE TO authenticated
USING (auth.uid() = user_id)
WITH CHECK (auth.uid() = user_id);

-- 5. Политика СОЗДАНИЯ (Критически важная!): 
-- Разрешаем сервису (или любому authenticated) создавать уведомления ДЛЯ ЛЮБОГО пользователя.
-- В рамках системы стадиона это безопасно, так как мы доверяем логике приложения.
DROP POLICY IF EXISTS "System_Insert_Notifications" ON notifications;
CREATE POLICY "System_Insert_Notifications" ON notifications
FOR INSERT TO authenticated
WITH CHECK (true);

--- Проверка: является ли текущий пользователь участником чата?
CREATE OR REPLACE FUNCTION public.check_is_member(c_id uuid)
RETURNS boolean
LANGUAGE sql SECURITY DEFINER SET search_path = public
AS $$
    SELECT EXISTS (
        SELECT 1 FROM public.conversation_participants
        WHERE conversation_id = c_id AND user_id = auth.uid()
    );
$$;

-- Проверка: является ли текущий пользователь админом чата?
CREATE OR REPLACE FUNCTION public.check_is_admin(c_id uuid)
RETURNS boolean
LANGUAGE sql SECURITY DEFINER SET search_path = public
AS $$
    SELECT EXISTS (
        SELECT 1 FROM public.conversation_participants
        WHERE conversation_id = c_id AND user_id = auth.uid() AND is_admin = true
    );
$$;

-- ==========================================================
-- 8. ПОЛИТИКИ ДОСТУПА (RLS) - ЧАТ [ОБНОВЛЕНО]
-- ==========================================================

-- --- CONVERSATIONS ---
DROP POLICY IF EXISTS "Chat_Select" ON public.conversations;
DROP POLICY IF EXISTS "Chat_Insert" ON public.conversations;
DROP POLICY IF EXISTS "Chat_Update" ON public.conversations;
DROP POLICY IF EXISTS "Chat_Delete" ON public.conversations;

CREATE POLICY "Chat_Select" ON public.conversations FOR SELECT TO authenticated
USING (
    public.has_permission('chat.use') AND
    (creator_id = auth.uid() OR public.check_is_member(id))
);

CREATE POLICY "Chat_Insert" ON public.conversations FOR INSERT TO authenticated 
WITH CHECK (
    public.has_permission('chat.use')
);

CREATE POLICY "Chat_Update" ON public.conversations FOR UPDATE TO authenticated
USING (
    public.has_permission('chat.use') AND
    (
        creator_id = auth.uid() 
        OR EXISTS (
            SELECT 1 FROM public.conversation_participants 
            WHERE conversation_id = conversations.id 
            AND user_id = auth.uid() 
            AND is_admin = true
        )
    )
);

CREATE POLICY "Chat_Delete" ON public.conversations FOR DELETE TO authenticated
USING (
    public.has_permission('chat.use') AND 
    creator_id = auth.uid()
);

-- --- MESSAGES ---
DROP POLICY IF EXISTS "Msg_Select" ON messages;
DROP POLICY IF EXISTS "Msg_Insert" ON messages;
DROP POLICY IF EXISTS "Msg_Update" ON messages;
DROP POLICY IF EXISTS "Msg_Delete" ON messages;

CREATE POLICY "Msg_Select" ON messages FOR SELECT TO authenticated
USING (
    public.has_permission('chat.use') AND
    EXISTS (SELECT 1 FROM public.conversation_participants WHERE conversation_id = messages.conversation_id AND user_id = auth.uid())
);

CREATE POLICY "Msg_Insert" ON messages FOR INSERT TO authenticated
WITH CHECK (
    public.has_permission('chat.use') AND
    sender_id = auth.uid() AND
    EXISTS (SELECT 1 FROM public.conversation_participants WHERE conversation_id = messages.conversation_id AND user_id = auth.uid())
);

CREATE POLICY "Msg_Update" ON messages FOR UPDATE TO authenticated
USING (
    public.has_permission('chat.use') AND
    EXISTS (SELECT 1 FROM public.conversation_participants WHERE conversation_id = messages.conversation_id AND user_id = auth.uid())
);

CREATE POLICY "Msg_Delete" ON messages FOR DELETE TO authenticated
USING (
    public.has_permission('chat.use') AND
    (
        sender_id = auth.uid() OR
        EXISTS (SELECT 1 FROM public.conversation_participants WHERE conversation_id = messages.conversation_id AND user_id = auth.uid() AND is_admin = true)
    )
    AND EXISTS (SELECT 1 FROM public.conversation_participants WHERE conversation_id = messages.conversation_id AND user_id = auth.uid())
);

-- --- CONVERSATION_PARTICIPANTS ---
DROP POLICY IF EXISTS "Part_Select" ON public.conversation_participants;
DROP POLICY IF EXISTS "Part_Insert" ON public.conversation_participants;
DROP POLICY IF EXISTS "Part_Update" ON public.conversation_participants;
DROP POLICY IF EXISTS "Part_Delete" ON public.conversation_participants;

CREATE POLICY "Part_Select" ON public.conversation_participants FOR SELECT TO authenticated
USING (
    public.has_permission('chat.use') AND
    (user_id = auth.uid() OR public.check_is_member(conversation_id))
);

CREATE POLICY "Part_Insert" ON public.conversation_participants FOR INSERT TO authenticated
WITH CHECK (
    public.has_permission('chat.use') AND
    (
        -- Пользователь является создателем самого чата
        EXISTS (
            SELECT 1 FROM public.conversations c 
            WHERE c.id = conversation_id AND c.creator_id = auth.uid()
        )
        OR 
        -- Или пользователь уже добавлен в этот чат и является админом
        public.check_is_admin(conversation_id)
    )
);

CREATE POLICY "Part_Update" ON public.conversation_participants FOR UPDATE TO authenticated
USING (
    public.has_permission('chat.use') AND
    public.check_is_admin(conversation_id)
);

CREATE POLICY "Part_Delete" ON public.conversation_participants FOR DELETE TO authenticated
USING (
    public.has_permission('chat.use') AND
    (user_id = auth.uid() OR public.check_is_admin(conversation_id))
);