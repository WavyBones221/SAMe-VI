using System.Reflection;

namespace SAMe_VI.Object
{

    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class RequiredFieldAttribute : Attribute
    {
        public double HardMin { get; init; } = 0.70;
        public double SoftMin { get; init; } = 0.90;
        public bool MissingConfidenceIsSoft { get; init; } = false;
    }

    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class PositiveNumberAttribute : Attribute
    {
        public double HardMin { get; init; } = 0.70;
        public double SoftMin { get; init; } = 0.90;
        public bool MissingConfidenceIsSoft { get; init; } = false;
        public bool AllowZero { get; init; } = false;
    }

    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class ValidateChildrenAttribute : Attribute
    {
        public int MinCount { get; init; } = 1;
        public double HardMin { get; init; } = 0.70;
        public double SoftMin { get; init; } = 0.90;
        public bool MissingConfidenceIsSoft { get; init; } = true;
        public string? ItemName { get; init; }
    }

    internal static class AttributeHelpers
    {
        public static IEnumerable<T> GetAttrs<T>(this PropertyInfo p) where T : Attribute
        {
            return p.GetCustomAttributes(typeof(T), true).Cast<T>();
        }
    }

}
