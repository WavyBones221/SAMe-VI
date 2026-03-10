using SAMe_VI.Object;
using SAMe_VI.Object.Models;

namespace SAMe_VI.Service.Validators
{

    internal static class ConfidenceChecks
    {
        public static void CheckRequired<T>(ValidationResult r, string path, ConfidenceValue<T> field, double hardMin, double softMin, bool missingConfidenceIsSoft)
        {
            if (field == null || field.Value == null)
            {
                r.AddForPath(path, "is required.");
                return;
            }

            if (field.Value is string s && string.IsNullOrWhiteSpace(s))
            {
                r.AddForPath(path, "is required.");
                return;
            }

            bool userValidated = field.userValidated != null && field.userValidated.Value;

            if (!userValidated)
            {
                double? conf = field.Confidence;

                if (conf == null)
                {
                    if (missingConfidenceIsSoft)
                    {
                        r.AddWarningForPath(path, $"confidence not provided.");
                    }
                    else
                    {
                        r.AddForPath(path, "confidence missing.");
                    }
                    return;
                }

                if (conf.Value < hardMin)
                {
                    r.AddForPath(path, $"{path} confidence too low: {conf.Value:0.###} (min {hardMin:0.###}).");
                }
                else if (conf.Value < softMin)
                {
                    r.AddWarningForPath(path, $"{path} confidence is low: {conf.Value:0.###}.");
                }
            }
            else return;
        }

        public static void CheckPositive(ValidationResult r, string path, ConfidenceValue<decimal> field, double hardMin, double softMin, bool missingConfidenceIsSoft, bool allowZero)
        {
            CheckRequired(r, path, field, hardMin, softMin, missingConfidenceIsSoft);
            decimal v = field != null ? field.Value : 0m;
            if (allowZero)
            {
                if (v < 0m)
                {
                    // \u2265 = ≥
                    r.AddForPath(path,$"{path} must be \u2265 0.");
                }
            }
            else
            {
                if (v <= 0m)
                {
                    r.AddForPath(path,$"{path} must be > 0.");
                }
            }
        }
    }

}
