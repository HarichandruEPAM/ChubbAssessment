using Microsoft.EntityFrameworkCore;
using PolicyManagement.Domain.Constants;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enums;

namespace PolicyManagement.Infrastructure.Data.Seeding;

public static class PolicyDataSeeder
{
    private static readonly string[] PolicyholderNames =
    {
        "Wei Zhang", "Li Na", "Chen Jing", "Wang Fang", "Liu Yang",
        "Zhao Lei", "Huang Min",
        "Tanaka Kenji", "Suzuki Yuki", "Watanabe Hiroshi", "Ito Sakura", "Yamamoto Daiki",
        "Kim Jisoo", "Park Minjun", "Lee Sooyeon", "Choi Hyunwoo", "Jung Yuna",
        "Priya Sharma", "Rajesh Kumar", "Ananya Singh", "Vikram Patel", "Deepa Nair",
        "Somchai Thongchai", "Napaporn Srisai", "Wanchai Boonsri",
        "Maria Santos", "Jose Reyes", "Ana Cruz", "Carlo Mendoza",
        "Ahmad Roslan", "Nurul Aisyah", "Razif Hashim",
        "Budi Santoso", "Siti Rahayu", "Agus Setiawan",
        "Tan Wei Ming", "Lim Beng Huat", "Ng Swee Lan",
        "Ho Siu Kwan", "Chan Wai Ying", "Cheung Kwok Fai",
        "David Nguyen", "Thi Lan Pham", "Minh Tuan Le"
    };

    private static readonly string[] Underwriters =
    {
        "Sarah Mitchell", "James Wong", "Priya Nair", "Robert Chen", "Emma Thompson",
        "Michael Tan", "Sophie Laurent", "Kenji Mori", "Aisha Patel", "Lucas Fernandez",
        "Mei Lin", "Daniel Kim", "Rachel Goldstein", "Arjun Sharma", "Nina Kowalski",
        "Marcus Lee", "Yuki Tanaka", "Claire Dubois", "Samuel Osei", "Fatima Al-Hassan"
    };

    private static readonly string[] Regions =
    {
        "Singapore", "Hong Kong", "Australia", "Japan",
        "Thailand", "Indonesia", "Malaysia", "Philippines"
    };

    private static readonly string[] Currencies =
    {
        Currency.USD, Currency.SGD, Currency.HKD,
        Currency.AUD, Currency.JPY, Currency.THB
    };

    private static readonly PolicyStatus[] Statuses =
    {
        PolicyStatus.Active, PolicyStatus.Expired, PolicyStatus.Pending, PolicyStatus.Cancelled
    };

    private static readonly LineOfBusiness[] LinesOfBusiness =
    {
        LineOfBusiness.Property, LineOfBusiness.Casualty,
        LineOfBusiness.AandH, LineOfBusiness.Marine
    };

    public static async Task SeedAsync(PolicyDbContext db)
    {
        if (await db.Policies.AnyAsync())
            return;

        const int recordCount = 250;
        var rng = new Random(42);
        var effectiveDateOrigin = new DateTime(2022, 1, 1);
        const int dateRangeDays = (2024 - 2022) * 365 + 365; // 2022-01-01 to 2024-12-31

        var policies = new List<Policy>(recordCount);
        // Fixed deterministic seed timestamp — every fresh database has identical CreatedAt/UpdatedAt values.
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < recordCount; i++)
        {
            var effectiveDate = effectiveDateOrigin.AddDays(rng.Next(0, dateRangeDays));
            var expiryDate = effectiveDate.AddYears(1);
            var premiumAmount = Math.Round((decimal)(rng.NextDouble() * (5_000_000 - 1_000) + 1_000), 2);

            policies.Add(new Policy
            {
                Id = Guid.NewGuid(),
                PolicyNumber = $"POL-{rng.Next(100000, 999999)}",
                PolicyholderName = PolicyholderNames[i % PolicyholderNames.Length],
                LineOfBusiness = LinesOfBusiness[i % LinesOfBusiness.Length],
                Status = Statuses[i % Statuses.Length],
                PremiumAmount = premiumAmount,
                Currency = Currencies[i % Currencies.Length],
                EffectiveDate = effectiveDate,
                ExpiryDate = expiryDate,
                Region = Regions[i % Regions.Length],
                Underwriter = Underwriters[i % Underwriters.Length],
                FlaggedForReview = rng.NextDouble() < 0.15,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await db.Policies.AddRangeAsync(policies);
        await db.SaveChangesAsync();
    }
}
