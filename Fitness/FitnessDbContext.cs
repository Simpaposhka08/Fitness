using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;

public class FitnessDbContext : DbContext
{
    public FitnessDbContext(DbContextOptions<FitnessDbContext> options) : base(options)
    {
    }
    public DbSet<User> Users { get; set; }
    public DbSet<Workout> Workouts { get; set; }
    public DbSet<PurchasedWorkout> PurchasedWorkouts { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Workout>()
           .Property(w => w.Price)
           .HasColumnType("numeric(18,2)");
        modelBuilder.Entity<Workout>().HasData(
            GenerateWorkoutsForDays(5)
        );

        modelBuilder.Entity<User>().HasData(
         new User
         {
             Id = 1,
             Name = "Admin",
             PhoneNumber = "77777777777",
             PasswordHash = "/Pe7bVRs+4LS5VSGmErnoYYqZmrLRB4M+LTtNKT8+dc=",  // Пароль Admin@2025
             IsAdmin = true
         }
     );
    }

    private static Workout[] GenerateWorkoutsForDays(int daysCount)
    {
        var workouts = new List<Workout>();
        // PostgreSQL требует UTC время для timestamp with time zone
        // Начинаем с сегодняшнего дня (DateTime.UtcNow.Date)
        var startDate = DateTime.UtcNow.Date;

        // Список названий тренировок на русском языке
        var workoutTitles = new[] {
        "Йога для начинающих",
        "Пилатес",
        "Силовая тренировка",
        "Кардио-тренировка",
        "Стретчинг",
        "Функциональный тренинг",
        "Танцевальная аэробика",
        "Кроссфит",
        "Аквааэробика",
        "Тай-чи"
    };

        // Список тренеров
        var trainerNames = new[] {
        "Анна Иванова", "Сергей Петров", "Мария Васильева", "Иван Кузнецов",
        "Дмитрий Смирнов", "Ольга Чернова", "Евгений Федоров", "Екатерина Лебедева",
        "Алексей Сидоров", "Светлана Борисова"
    };

        // Список ссылок на видео для каждой тренировки
        var videoUrls = new[] {
        "https://www.youtube.com/watch?v=v7AYKMP6rOE", // Йога для начинающих
        "https://www.youtube.com/watch?v=IZ8j8g44ndM", // Пилатес
        "https://www.youtube.com/watch?v=ml6cT4AZdqI", // Силовая тренировка
        "https://www.youtube.com/watch?v=UBMk30rjy0o", // Кардио-тренировка
        "https://www.youtube.com/watch?v=g_tea8ZNk5A", // Стретчинг
        "https://www.youtube.com/watch?v=tzN9ypZwAJA", // Функциональный тренинг
        "https://www.youtube.com/watch?v=cyXGeryXFn4", // Танцевальная аэробика
        "https://www.youtube.com/watch?v=mlVr9CoFvJA", // Кроссфит
        "https://www.youtube.com/watch?v=4H9Ie0tXw_8", // Аквааэробика
        "https://www.youtube.com/watch?v=2q9z6f0o8XY"  // Тай-чи
    };

        // Список Slugs для каждой тренировки (названия на английском)
        var slugs = new[] {
        "yoga-beginners", // Йога для начинающих
        "pilates",  // Пилатес
        "strength-training",   // Силовая тренировка
        "cardio-workout",   // Кардио-тренировка
        "stretching", // Стретчинг
        "functional-training", // Функциональный тренинг
        "dance-aerobics", // Танцевальная аэробика
        "crossfit", // Кроссфит
        "aqua-aerobics", // Аквааэробика
        "tai-chi" // Тай-чи
    };

        // Список описаний для каждой тренировки
        var workoutDescriptions = new[] {
        "Йога для начинающих — идеальный способ начать свой путь к здоровому образу жизни. Изучите базовые асаны, техники дыхания и расслабления. Подходит для всех уровней подготовки.",
        "Пилатес — система упражнений, направленная на укрепление мышц кора, улучшение осанки и гибкости. Идеально для тех, кто хочет развить силу и выносливость без излишней нагрузки на суставы.",
        "Силовая тренировка — комплекс упражнений с отягощениями для развития мышечной силы и выносливости. Помогает укрепить все группы мышц и улучшить общую физическую форму.",
        "Кардио-тренировка — интенсивные упражнения для улучшения работы сердечно-сосудистой системы, сжигания калорий и повышения выносливости. Отлично подходит для похудения и поддержания формы.",
        "Стретчинг — комплекс упражнений на растяжку мышц и улучшение гибкости. Помогает снять напряжение, улучшить осанку и предотвратить травмы. Подходит для всех возрастов.",
        "Функциональный тренинг — упражнения, имитирующие повседневные движения. Развивает силу, координацию и баланс. Помогает улучшить качество жизни и снизить риск травм в быту.",
        "Танцевальная аэробика — веселая и энергичная тренировка под музыку. Сочетает кардионагрузку с танцевальными движениями. Отлично поднимает настроение и помогает сжечь калории.",
        "Кроссфит — высокоинтенсивная функциональная тренировка, сочетающая элементы гимнастики, тяжелой атлетики и кардио. Развивает силу, выносливость и скорость.",
        "Аквааэробика — тренировка в воде, идеальная для людей всех возрастов и уровней подготовки. Снижает нагрузку на суставы, улучшает гибкость и укрепляет мышцы.",
        "Тай-чи — древняя китайская практика, сочетающая медленные движения, медитацию и дыхательные упражнения. Улучшает баланс, гибкость и психическое здоровье."
    };

        for (int day = 0; day < daysCount; day++)
        {
            for (int i = 0; i < 10; i++) // Генерация 10 тренировок на каждый день
            {
                var workout = new Workout
                {
                    Id = (day * 10) + i + 1, // Генерация уникального Id
                    Title = workoutTitles[i % workoutTitles.Length], // Название тренировки
                    Description = workoutDescriptions[i % workoutDescriptions.Length], // Описание тренировки
                    Trainer = trainerNames[i % trainerNames.Length], // Назначаем тренера
                    Price = (i + 1) * 100, // Цена тренировки
                    // Используем UTC время для PostgreSQL (startDate уже UTC, поэтому просто создаем DateTime с Kind=Utc)
                    StartTime = DateTime.SpecifyKind(startDate.AddDays(day).AddHours(9 + i), DateTimeKind.Utc),
                    EndTime = DateTime.SpecifyKind(startDate.AddDays(day).AddHours(10 + i), DateTimeKind.Utc),
                    Slug = slugs[i % slugs.Length], // Генерация Slug на основе списка
                    VideoUrl = videoUrls[i % videoUrls.Length] // Присваиваем ссылку на видео
                };
                workouts.Add(workout);
            }
        }
        return workouts.ToArray();
    }
}
