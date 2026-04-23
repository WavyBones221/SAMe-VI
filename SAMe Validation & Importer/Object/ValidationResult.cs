using Newtonsoft.Json.Linq;

namespace SAMe_VI.Object
{
    internal sealed class FieldIssues
    {
        public List<string> Errors { get; } = [];
        public List<string> Warnings { get; } = [];

        public bool HasErrors => Errors.Count > 0;
        public bool HasWarnings => Warnings.Count > 0;
    }

    internal sealed class ValidationResult
    {
        private readonly List<string> _errors = [];
        private readonly List<string> _warnings = [];
        private readonly Dictionary<string, FieldIssues> _byPath = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Errors => _errors;
        public IReadOnlyList<string> Warnings => _warnings;
        public IReadOnlyDictionary<string, FieldIssues> FieldIssuesByPath => _byPath;
        public bool IsValid => _errors.Count == 0;

        public void Add(string message)
        {
            _errors.Add(message);
        }

        public void AddWarning(string message)
        {
            _warnings.Add(message);
        }

        public void AddForPath(string path, string message)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _errors.Add(message);
                return;
            }

            FieldIssues bag;
            if (!_byPath.TryGetValue(path, out bag!))
            {
                bag = new FieldIssues();
                _byPath[path] = bag;
            }

            bag.Errors.Add(message);
            _errors.Add($"{path}: {message}");
        }

        public void AddWarningForPath(string path, string message)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _warnings.Add(message);
                return;
            }

            FieldIssues bag;
            if (!_byPath.TryGetValue(path, out bag!))
            {
                bag = new FieldIssues();
                _byPath[path] = bag;
            }

            bag.Warnings.Add(message);
            _warnings.Add($"{path}: {message}");
        }

        public IReadOnlyList<string> GetErrorsForPath(string path)
        {
            FieldIssues bag;
            if (_byPath.TryGetValue(path, out bag!))
            {
                return bag.Errors;
            }
            return [];
        }

        public IReadOnlyList<string> GetWarningsForPath(string path)
        {
            FieldIssues bag;
            if (_byPath.TryGetValue(path, out bag!))
            {
                return bag.Warnings;
            }
            return [];
        }

        public void Merge(ValidationResult other)
        {
            _errors.AddRange(other._errors);
            _warnings.AddRange(other._warnings);

            foreach (KeyValuePair<string, FieldIssues> kv in other._byPath)
            {
                FieldIssues bag;
                if (!_byPath.TryGetValue(kv.Key, out bag!))
                {
                    bag = new FieldIssues();
                    _byPath[kv.Key] = bag;
                }

                bag.Errors.AddRange(kv.Value.Errors);
                bag.Warnings.AddRange(kv.Value.Warnings);
            }
        }

        public Dictionary<string, JObject> BuildIssueJProperties()
        {
            Dictionary<string, JObject> result = [];

            foreach (KeyValuePair<string, FieldIssues> kv in _byPath)
            {
                JObject issueObj = [];

                if (kv.Value.Errors.Count > 0)
                {
                    issueObj["errors"] = new JArray(kv.Value.Errors);
                }

                if (kv.Value.Warnings.Count > 0)
                {
                    issueObj["warnings"] = new JArray(kv.Value.Warnings);
                }

                result[kv.Key] = issueObj;
            }

            return result;
        }

        public void AttachToJson(JObject root, out JObject output)
        {
            output = root;

            Dictionary<string, JObject> props = BuildIssueJProperties();

            foreach (KeyValuePair<string, JObject> entry in props)
            {
                string path = entry.Key;
                JObject issueObj = entry.Value;

                JToken? fieldToken = output[path];

                if (fieldToken == null)
                {
                    int lastUnderscore = path.LastIndexOf('_');
                    if (lastUnderscore > 0 && lastUnderscore < path.Length - 1)
                    {
                        string fieldName = path.Substring(0, lastUnderscore);
                        string lineSuffix = path.Substring(lastUnderscore + 1);

                        if (int.TryParse(lineSuffix, out int lineNumber))
                        {
                            int index = lineNumber - 1;

                            if (index >= 0)
                            {
                                JToken? orderLinesToken = output["OrderLines"];
                                JToken? valueArrayToken = orderLinesToken?["valueArray"];

                                if (valueArrayToken is JArray lineArray)
                                {
                                    if (index < lineArray.Count)
                                    {
                                        JToken wrapperToken = lineArray[index];
                                        JObject? wrapperObject = wrapperToken as JObject;

                                        JObject? valueObj = wrapperObject?["valueObject"] as JObject;
                                        if (valueObj != null)
                                        {
                                            fieldToken = valueObj[fieldName];
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (fieldToken is JObject obj)
                {
                    JToken? errorsToken = issueObj["errors"];
                    if (errorsToken != null)
                    {
                        obj["errors"] = errorsToken;
                    }

                    JToken? warningsToken = issueObj["warnings"];
                    if (warningsToken != null)
                    {
                        obj["warnings"] = warningsToken;
                    }
                }
            }
        }
    }
}