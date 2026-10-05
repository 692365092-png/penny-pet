using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace PennyPet
{
    // Serializes CheckResult objects directly. A report field can no longer be
    // hand-wired to an unrelated boolean: the field that stores the check is
    // the field that is emitted and aggregated into the root result.
    internal sealed class SelfTestReport
    {
        private readonly SortedDictionary<string, object> _checks =
            new SortedDictionary<string, object>(StringComparer.Ordinal);
        private readonly List<string> _falsePaths = new List<string>();
        private bool _ok = true;

        internal bool Ok { get { return _ok; } }
        internal IList<string> FalsePaths { get { return _falsePaths.AsReadOnly(); } }

        internal void Add(object checkResult)
        {
            if (checkResult == null)
                throw new ArgumentNullException(nameof(checkResult));
            Type type = checkResult.GetType();
            if (!IsCheckResultType(type))
                throw new InvalidOperationException(
                    "Self-test report roots must end in CheckResult: " +
                    type.FullName);
            string section = ResultSectionName(type);
            if (_checks.ContainsKey(section))
                throw new InvalidOperationException(
                    "Duplicate self-test report section: " + section);
            _checks.Add(section, SerializeCheckResult(checkResult, section));
        }

        internal string ToJson()
        {
            SortedDictionary<string, object> root =
                new SortedDictionary<string, object>(StringComparer.Ordinal);
            root.Add("ok", _ok);
            root.Add("checks", _checks);
            return new JavaScriptSerializer().Serialize(root) + Environment.NewLine;
        }

        private object SerializeCheckResult(object owner, string path)
        {
            SortedDictionary<string, object> fields =
                new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (FieldInfo field in owner.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic))
            {
                string key = ToSnakeCase(field.Name);
                object value = field.GetValue(owner);
                Type fieldType = field.FieldType;
                if (fieldType == typeof(bool))
                {
                    bool passed = value != null && (bool)value;
                    fields[key] = passed;
                    if (!passed)
                    {
                        _ok = false;
                        _falsePaths.Add(path + "." + key);
                    }
                    continue;
                }
                if (value != null && IsCheckResultType(value.GetType()))
                {
                    fields[key] = SerializeCheckResult(value,
                        path + "." + key);
                    continue;
                }
                object simple;
                if (TrySerializeMetadata(value, fieldType, out simple))
                    fields[key] = simple;
            }
            return fields;
        }

        private static bool TrySerializeMetadata(object value, Type type,
            out object serialized)
        {
            serialized = null;
            if (value == null) return false;
            Type actual = Nullable.GetUnderlyingType(type) ?? type;
            if (actual == typeof(string) || actual == typeof(char) ||
                actual == typeof(byte) || actual == typeof(sbyte) ||
                actual == typeof(short) || actual == typeof(ushort) ||
                actual == typeof(int) || actual == typeof(uint) ||
                actual == typeof(long) || actual == typeof(ulong) ||
                actual == typeof(float) || actual == typeof(double) ||
                actual == typeof(decimal))
            {
                serialized = value;
                return true;
            }
            if (actual == typeof(DateTime))
            {
                serialized = ((DateTime)value).ToUniversalTime()
                    .ToString("o");
                return true;
            }
            if (actual.IsEnum)
            {
                serialized = value.ToString();
                return true;
            }
            IEnumerable sequence = value as IEnumerable;
            if (sequence == null || value is string) return false;
            List<object> items = new List<object>();
            foreach (object item in sequence)
            {
                if (item == null)
                {
                    items.Add(null);
                    continue;
                }
                object simpleItem;
                if (!TrySerializeMetadata(item, item.GetType(),
                    out simpleItem)) return false;
                items.Add(simpleItem);
            }
            serialized = items;
            return true;
        }

        private static bool IsCheckResultType(Type type)
        {
            return type != null && type.Name.EndsWith("CheckResult",
                StringComparison.Ordinal);
        }

        private static string ResultSectionName(Type type)
        {
            const string suffix = "CheckResult";
            string name = type.Name;
            if (name.EndsWith(suffix, StringComparison.Ordinal))
                name = name.Substring(0, name.Length - suffix.Length);
            return ToSnakeCase(name);
        }

        internal static string ToSnakeCase(string value)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            StringBuilder result = new StringBuilder(value.Length + 8);
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                bool upper = Char.IsUpper(current);
                if (upper && index > 0)
                {
                    char previous = value[index - 1];
                    bool previousLowerOrDigit = Char.IsLower(previous) ||
                        Char.IsDigit(previous);
                    bool nextLower = index + 1 < value.Length &&
                        Char.IsLower(value[index + 1]);
                    if (previousLowerOrDigit ||
                        (Char.IsUpper(previous) && nextLower))
                        result.Append('_');
                }
                result.Append(Char.ToLowerInvariant(current));
            }
            return result.ToString();
        }
    }
}
