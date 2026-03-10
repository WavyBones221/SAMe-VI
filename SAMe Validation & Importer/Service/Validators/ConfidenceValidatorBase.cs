using SAMe_VI.Object;
using SAMe_VI.Object.Models;
using System.Collections;
using System.Reflection;

namespace SAMe_VI.Service.Validators
{

    internal abstract class ConfidenceValidatorBase<TRoot> : IValidator<TRoot>
    {
        public Task<ValidationResult> ValidateAsync(TRoot doc, CancellationToken ct = default)
        {
            ValidationResult r = new();
            ValidateObject(r, doc!, typeof(TRoot).Name);
            return PostValidateAsync(r, doc, ct);
        }

        protected virtual Task<ValidationResult> PostValidateAsync(ValidationResult r, TRoot doc, CancellationToken ct)
        {
            return Task.FromResult(r);
        }

        private void ValidateObject(ValidationResult r, object obj, string path)
        {
            if (obj is null)
            {
                r.AddForPath(path, $"{path} is null.");
                return;
            }

            Type t = obj.GetType();
            PropertyInfo[] props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public);

            for (int i = 0; i < props.Length; i++)
            {
                PropertyInfo prop = props[i];

                if (prop.GetIndexParameters().Length > 0 || !prop.CanRead || prop.GetMethod == null || prop.GetMethod.GetParameters().Length > 0) 
                {
                    continue;
                }

                string propPath = $"{path}.{prop.Name}";
                object? value = prop.GetValue(obj);

                if (IsConfidenceValue(prop.PropertyType))
                {
                    ValidateConfidenceValueProperty(r, prop, value, propPath);
                    continue;
                }

                if (value != null && IsComplexType(prop.PropertyType))
                {
                    ValidateObject(r, value, propPath);
                }
            }
        }

        private static bool IsConfidenceValue(Type t)
        {
            return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ConfidenceValue<>);
        }

        private static bool IsComplexType(Type t)
        {
            if (t.IsPrimitive || t.IsEnum)
            {
                return false;
            }

            if (t == typeof(string) || t == typeof(DateTime) || t == typeof(decimal) || t == typeof(Guid))
            {
                return false;
            }

            return true;
        }

        private void ValidateConfidenceValueProperty(ValidationResult r, PropertyInfo prop, object? cvObj, string path)
        {
            if (cvObj is null)
            {
                r.AddForPath(path, $"{path} is missing.");
                return;
            }

            Type cvType = prop.PropertyType;
            Type tArg = cvType.GetGenericArguments()[0];

            RequiredFieldAttribute? required = GetSingle<RequiredFieldAttribute>(prop);
            PositiveNumberAttribute? positive = GetSingle<PositiveNumberAttribute>(prop);
            ValidateChildrenAttribute? children = GetSingle<ValidateChildrenAttribute>(prop);

            if (tArg == typeof(string))
            {
                if (required != null)
                {
                    MethodInfo method = typeof(ConfidenceChecks).GetMethod(nameof(ConfidenceChecks.CheckRequired))!.MakeGenericMethod(typeof(string));
                    object[] args = [r, path, cvObj, required.HardMin, required.SoftMin, required.MissingConfidenceIsSoft];
                    method.Invoke(null, args);
                }
            }
            else if (tArg == typeof(DateTime))
            {
                if (required != null)
                {
                    MethodInfo method = typeof(ConfidenceChecks).GetMethod(nameof(ConfidenceChecks.CheckRequired))!.MakeGenericMethod(typeof(DateTime));
                    object[] args = [r, path, cvObj, required.HardMin, required.SoftMin, required.MissingConfidenceIsSoft];
                    method.Invoke(null, args);
                }
            }
            else if (tArg == typeof(decimal))
            {
                if (positive != null)
                {
                    ConfidenceChecks.CheckPositive(r, path, (ConfidenceValue<decimal>)cvObj, positive.HardMin, positive.SoftMin, positive.MissingConfidenceIsSoft, positive.AllowZero);
                }
                else if (required != null)
                {
                    MethodInfo method = typeof(ConfidenceChecks).GetMethod(nameof(ConfidenceChecks.CheckRequired))!.MakeGenericMethod(typeof(decimal));
                    object[] args = [r, path, cvObj, required.HardMin, required.SoftMin, required.MissingConfidenceIsSoft];
                    method.Invoke(null, args);
                }
            }
            else if (tArg.IsGenericType && tArg.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (children != null)
                {
                    PropertyInfo confProp = cvObj.GetType().GetProperty(nameof(ConfidenceValue<object>.Confidence))!;
                    double? conf = (double?)confProp.GetValue(cvObj);
                    if (conf is null)
                    {
                        if (children.MissingConfidenceIsSoft)
                        {
                            r.AddWarningForPath(path,$"{path} confidence not provided.");
                        }
                        else
                        {
                            r.AddForPath(path,$"{path} confidence missing.");
                        }
                    }
                    else
                    {
                        if (conf.Value < children.HardMin)
                        {
                            r.AddForPath(path,$"{path} confidence too low: {conf.Value:0.###} (min {children.HardMin:0.###} ).");
                        }
                        else if (conf.Value < children.SoftMin)
                        {
                            r.AddWarningForPath(path,$"{path} confidence is low: {conf.Value:0.###}.");
                        }
                    }

                    PropertyInfo listProp = cvObj.GetType().GetProperty(nameof(ConfidenceValue<object>.Value))!;
                    object? listObj = listProp.GetValue(cvObj);

                    int count = 0;
                    if (listObj is IEnumerable list)
                    {
                        int index = 0;
                        foreach (object? item in list)
                        {
                            count++;
                            ValidateObject(r, item!, $"{path}[{index}]");
                            index++;
                        }
                    }

                    if (count < children.MinCount)
                    {
                        string name = string.IsNullOrWhiteSpace(children.ItemName) ? prop.Name : children.ItemName!;
                        r.AddForPath(path,$"{name} must contain at least {children.MinCount} item(s).");
                    }
                }
            }
            else
            {
                if (required != null)
                {
                    MethodInfo method = typeof(ConfidenceChecks).GetMethod(nameof(ConfidenceChecks.CheckRequired))!.MakeGenericMethod(tArg);
                    object[] args = [r, path, cvObj, required.HardMin, required.SoftMin, required.MissingConfidenceIsSoft];
                    method.Invoke(null, args);
                }
            }
        }

        private static TAttr? GetSingle<TAttr>(PropertyInfo p) where TAttr : Attribute
        {
            IEnumerable<TAttr> all = p.GetCustomAttributes(typeof(TAttr), true).Cast<TAttr>();
            foreach (TAttr item in all)
            {
                return item;
            }
            return null;
        }
    }

}
