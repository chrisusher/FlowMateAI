using System.Reflection;
using System.Text.Json.Serialization;

namespace Services.Tests.Serialization;

public sealed class EnumJsonTests
{
    public static IEnumerable<Type> EnumTypes => typeof(BillingPlan).Assembly.GetTypes().Where(type => type.IsEnum);

    [TestCaseSource(nameof(EnumTypes))]
    public void DefaultSerializerUsesDeclaredEnumNamesAndRoundTripsEveryValue(Type enumType)
    {
        foreach (var value in Enum.GetValues(enumType))
        {
            var name = Enum.GetName(enumType, value)!;
            var expected = enumType.GetField(name)!.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? name;

            var json = JsonSerializer.Serialize(value, enumType);

            Assert.That(json, Is.EqualTo(JsonSerializer.Serialize(expected)), $"{enumType.Name}.{name}");
            Assert.That(JsonSerializer.Deserialize(json, enumType), Is.EqualTo(value));
        }
    }
}
