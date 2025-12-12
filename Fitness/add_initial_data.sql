-- SQL скрипт для добавления начальных данных
-- Выполните этот скрипт в pgAdmin 4 Query Tool
-- Этот скрипт безопасно добавляет данные, если их еще нет

-- 1. Добавление администратора (если его еще нет)
INSERT INTO "Users" ("Id", "Name", "PhoneNumber", "PasswordHash", "IsAdmin")
VALUES (1, 'Admin', '77777777777', '/Pe7bVRs+4LS5VSGmErnoYYqZmrLRB4M+LTtNKT8+dc=', true)
ON CONFLICT ("Id") DO NOTHING;

-- 2. Добавление тренировок (50 тренировок на 5 дней)
-- Используем DO блок для генерации всех тренировок
DO $$
DECLARE
    workout_titles TEXT[] := ARRAY[
        'Йога для начинающих',
        'Пилатес',
        'Силовая тренировка',
        'Кардио-тренировка',
        'Стретчинг',
        'Функциональный тренинг',
        'Танцевальная аэробика',
        'Кроссфит',
        'Аквааэробика',
        'Тай-чи'
    ];
    trainers TEXT[] := ARRAY[
        'Анна Иванова',
        'Сергей Петров',
        'Мария Васильева',
        'Иван Кузнецов',
        'Дмитрий Смирнов',
        'Ольга Чернова',
        'Евгений Федоров',
        'Екатерина Лебедева',
        'Алексей Сидоров',
        'Светлана Борисова'
    ];
    slugs TEXT[] := ARRAY[
        'yoga-beginners',
        'pilates',
        'strength-training',
        'cardio-workout',
        'stretching',
        'functional-training',
        'dance-aerobics',
        'crossfit',
        'aqua-aerobics',
        'tai-chi'
    ];
    video_urls TEXT[] := ARRAY[
        'https://www.youtube.com/watch?v=v7AYKMP6rOE',
        'https://www.youtube.com/watch?v=IZ8j8g44ndM',
        'https://www.youtube.com/watch?v=ml6cT4AZdqI',
        'https://www.youtube.com/watch?v=UBMk30rjy0o',
        'https://www.youtube.com/watch?v=g_tea8ZNk5A',
        'https://www.youtube.com/watch?v=tzN9ypZwAJA',
        'https://www.youtube.com/watch?v=cyXGeryXFn4',
        'https://www.youtube.com/watch?v=mlVr9CoFvJA',
        'https://www.youtube.com/watch?v=4H9Ie0tXw_8',
        'https://www.youtube.com/watch?v=2q9z6f0o8XY'
    ];
    descriptions TEXT[] := ARRAY[
        'Йога для начинающих — идеальный способ начать свой путь к здоровому образу жизни. Изучите базовые асаны, техники дыхания и расслабления. Подходит для всех уровней подготовки.',
        'Пилатес — система упражнений, направленная на укрепление мышц кора, улучшение осанки и гибкости. Идеально для тех, кто хочет развить силу и выносливость без излишней нагрузки на суставы.',
        'Силовая тренировка — комплекс упражнений с отягощениями для развития мышечной силы и выносливости. Помогает укрепить все группы мышц и улучшить общую физическую форму.',
        'Кардио-тренировка — интенсивные упражнения для улучшения работы сердечно-сосудистой системы, сжигания калорий и повышения выносливости. Отлично подходит для похудения и поддержания формы.',
        'Стретчинг — комплекс упражнений на растяжку мышц и улучшение гибкости. Помогает снять напряжение, улучшить осанку и предотвратить травмы. Подходит для всех возрастов.',
        'Функциональный тренинг — упражнения, имитирующие повседневные движения. Развивает силу, координацию и баланс. Помогает улучшить качество жизни и снизить риск травм в быту.',
        'Танцевальная аэробика — веселая и энергичная тренировка под музыку. Сочетает кардионагрузку с танцевальными движениями. Отлично поднимает настроение и помогает сжечь калории.',
        'Кроссфит — высокоинтенсивная функциональная тренировка, сочетающая элементы гимнастики, тяжелой атлетики и кардио. Развивает силу, выносливость и скорость.',
        'Аквааэробика — тренировка в воде, идеальная для людей всех возрастов и уровней подготовки. Снижает нагрузку на суставы, улучшает гибкость и укрепляет мышцы.',
        'Тай-чи — древняя китайская практика, сочетающая медленные движения, медитацию и дыхательные упражнения. Улучшает баланс, гибкость и психическое здоровье.'
    ];
    workout_id INTEGER := 1;
    day_offset INTEGER;
    hour_offset INTEGER;
    workout_date DATE;
    start_time TIMESTAMP WITH TIME ZONE;
    end_time TIMESTAMP WITH TIME ZONE;
BEGIN
    -- Генерируем тренировки для 5 дней (начиная с сегодняшнего дня)
    FOR day_offset IN 0..4 LOOP
        FOR hour_offset IN 0..9 LOOP
            -- Вычисляем дату и время (UTC) - начинаем с сегодняшнего дня
            workout_date := (CURRENT_DATE + (day_offset || ' days')::INTERVAL)::DATE;
            start_time := (workout_date + (9 + hour_offset || ' hours')::INTERVAL)::TIMESTAMP WITH TIME ZONE;
            end_time := (workout_date + (10 + hour_offset || ' hours')::INTERVAL)::TIMESTAMP WITH TIME ZONE;
            
            -- Вставляем тренировку только если её еще нет (по Id)
            INSERT INTO "Workouts" (
                "Id",
                "Title",
                "Description",
                "Trainer",
                "Price",
                "StartTime",
                "EndTime",
                "Slug",
                "VideoUrl"
            ) VALUES (
                workout_id,
                workout_titles[(hour_offset % 10) + 1],
                descriptions[(hour_offset % 10) + 1],
                trainers[(hour_offset % 10) + 1],
                (hour_offset + 1) * 100.00,
                start_time,
                end_time,
                slugs[(hour_offset % 10) + 1],
                video_urls[(hour_offset % 10) + 1]
            )
            ON CONFLICT ("Id") DO NOTHING; -- Не добавляем, если Id уже существует
            
            workout_id := workout_id + 1;
        END LOOP;
    END LOOP;
    
    -- Обновляем последовательность для Id
    PERFORM setval('"Workouts_Id_seq"', GREATEST((SELECT MAX("Id") FROM "Workouts"), workout_id - 1), true);
    
    RAISE NOTICE 'Проверено % тренировок. Добавлены только новые.', workout_id - 1;
END $$;

-- 3. Обновление последовательности для Users
SELECT setval('"Users_Id_seq"', GREATEST((SELECT MAX("Id") FROM "Users"), 1), true);

-- 4. Проверка: сколько данных добавлено
SELECT 
    (SELECT COUNT(*) FROM "Users") as "Пользователей",
    (SELECT COUNT(*) FROM "Workouts") as "Тренировок",
    (SELECT COUNT(*) FROM "PurchasedWorkouts") as "Записей на тренировки";

