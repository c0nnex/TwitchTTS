using System.Collections.Generic;
using System.Linq;

namespace System
{
    public static class ObjectExtensions
    {
        public static string ToString(this Object obj, IFormatProvider formatter)
        {
            if (obj != null)
                return (string)Convert.ChangeType(obj, typeof(string), System.Globalization.CultureInfo.InvariantCulture);
            return null;
        }
    }

    public static class StringExtensions
    {
        public static string GetPart(this string s, int part, string splitter)
        {
            var parts = s?.Split(new string[] { splitter }, StringSplitOptions.None);
            if (parts == null || part >= parts.Length)
                return String.Empty;
            return parts[part];
        }

        public static string ReplacePart(this string s, int part, string splitter, string newVal)
        {
            var parts = s?.Split(new string[] { splitter }, StringSplitOptions.None);
            if (parts == null || part >= parts.Length)
                return s;
            parts[part] = newVal;
            return String.Join(splitter, parts);
        }

        public static string SkipParts(this string s, int startPart, int numParts, string splitter)
        {
            var parts = new List<string>(s?.Split(new string[] { splitter }, StringSplitOptions.None));
            while ((startPart < parts.Count) && (numParts > 0))
            {
                parts.RemoveAt(startPart);
                numParts--;
            }
            if (parts.Count == 0)
                return String.Empty;
            return String.Join(splitter, parts);
        }

        public static String[] Split(this string inStr, String separator)
        {
            return inStr.Split(new string[] { separator }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static String ToHexString(this UInt32 num)
        {
            return String.Format("{0}:{1}", ((num & 0xFFFF0000) >> 16).ToString("X4"), (num & 0x0000ffff).ToString("X4"));
        }
        public static String ToHexString(this Int32 num)
        {
            return String.Format("{0}:{1}", ((num & 0xFFFF0000) >> 16).ToString("X4"), (num & 0x0000ffff).ToString("X4"));
        }
        public static String ToHexString(this ushort num)
        {
            return String.Format("{0}", (num & 0x0000ffff).ToString("X4"));
        }

        public static bool CompareNoCase(this string s, string other)
        {
            return !string.IsNullOrEmpty(s) && (string.Compare(s, other, true) == 0);
        }

        public static bool CaseInsensitiveCompare(this string s, string other)
        {
            return !string.IsNullOrEmpty(s) && (string.Compare(s, other, true) == 0);
        }

        public static bool IsNullOrEmpty(this string s)
        {
            return string.IsNullOrEmpty(s);
        }

        public static bool IsNull(this object o)
        {
            return (o == null);
        }

        public static bool ContainsNoCase(this IEnumerable<string> list, string value)
        {
            return list.FirstOrDefault(t => string.Compare(t, value, true) == 0) != null;
        }

        public static string ToLocalDateTimeString(this DateTime dt)
        {
            return dt.ToLocalTime().ToString();
        }

    }

}
