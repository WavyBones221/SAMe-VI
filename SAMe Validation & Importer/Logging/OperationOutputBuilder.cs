using SAMe_VI.Object;
using SAMe_VI.Object.Models;
using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace SAMe_VI.Logging
{

    internal sealed class ValueWithConfidence
    {
        public object? Value { get; init; }
        public double? Confidence { get; init; }
        public List<string> Errors { get; init; } = [];
        public List<string> Warnings { get; init; } = [];
    }

    internal sealed class DocumentProcessLog
    {
        public string CorrelationId { get; init; } = string.Empty;
        public string SourceFile { get; init; } = string.Empty;
        public string DocumentType { get; init; } = string.Empty;
        public DateTimeOffset ReceivedAtUtc { get; init; }
        public DateTimeOffset FinishedAtUtc { get; init; }

        public string? OrderNumber { get; init; }

        public object Input { get; init; } = new object();
        public object Extracted { get; init; } = new object();

        public List<string> Errors { get; init; } = [];
        public List<string> Warnings { get; init; } = [];
    }

    internal static class ProcessingLogBuilder
    {
        public static DocumentProcessLog BuildForSalesOrder(
            string sourceFile,
            string originalJson,
            SalesOrderRaw raw,
            ValidationResult validation,
            string documentType)
        {
            string corr = Guid.NewGuid().ToString("N");
            object input = JsonDocument.Parse(string.IsNullOrWhiteSpace(originalJson) ? "{}" : originalJson).RootElement.Clone();
            object extracted = SnapshotNode(raw, validation, string.Empty)!;

            DocumentProcessLog log = new()
            {
                CorrelationId = corr,
                SourceFile = sourceFile,
                DocumentType = documentType,
                ReceivedAtUtc = DateTimeOffset.UtcNow,
                FinishedAtUtc = DateTimeOffset.UtcNow,
                OrderNumber = raw.OrderNumber?.Value,
                Input = input,
                Extracted = extracted,
                Errors = [.. validation.Errors],
                Warnings = [.. validation.Warnings]
            };

            return log;
        }

        private static object? SnapshotNode(object? node, ValidationResult vr, string path)
        {
            if (node is null)
            {
                return null;
            }

            Type t = node.GetType();

            if (IsConfidenceValue(t))
            {
                return SnapshotConfidenceValue(node, vr, path);
            }

            if (node is string)
            {
                return node;
            }

            if (t.IsPrimitive || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t.IsEnum)
            {
                return node;
            }

            if (node is IEnumerable enumerable && node is not IDictionary)
            {
                List<object?> list = [];
                int idx = 0;
                foreach (object? item in enumerable)
                {
                    string itemPath = $"{path}[{idx}]";
                    list.Add(SnapshotNode(item, vr, itemPath));
                    idx++;
                }
                return list;
            }

            Dictionary<string, object?> map = new(StringComparer.OrdinalIgnoreCase);
            PropertyInfo[] props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < props.Length; i++)
            {
                PropertyInfo p = props[i];
                if (p.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object? value = p.GetValue(node);
                string childPath = string.IsNullOrEmpty(path) ? p.Name : $"{path}.{p.Name}";
                map[p.Name] = SnapshotNode(value, vr, childPath);
            }

            return map;
        }

        private static bool IsConfidenceValue(Type t)
        {
            return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ConfidenceValue<>);
        }

        private static ValueWithConfidence SnapshotConfidenceValue(object cvObj, ValidationResult vr, string path)
        {
            Type cvType = cvObj.GetType();
            PropertyInfo? valueProp = cvType.GetProperty("Value");
            PropertyInfo? confProp = cvType.GetProperty("Confidence");

            object? rawValue = valueProp?.GetValue(cvObj);
            object? snappedValue;

            if (rawValue is IEnumerable enumerable && rawValue is not string)
            {
                List<object?> list = [];
                int idx = 0;
                foreach (object? item in enumerable)
                {
                    string itemPath = $"{path}[{idx}]";
                    list.Add(SnapshotNode(item, vr, itemPath));
                    idx++;
                }
                snappedValue = list;
            }
            else
            {
                snappedValue = SnapshotNode(rawValue, vr, path);
            }

            double? confidence = null;
            if (confProp != null)
            {
                object? c = confProp.GetValue(cvObj);
                if (c is double d)
                {
                    confidence = d;
                }
            }

            ValueWithConfidence result = new()
            {
                Value = snappedValue,
                Confidence = confidence,
                Errors = [.. vr.GetErrorsForPath(path)],
                Warnings = [.. vr.GetWarningsForPath(path)]
            };

            return result;
        }
    }

}
