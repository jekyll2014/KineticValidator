// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++, C#, and Java: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Newtonsoft.Json;

namespace JsonEditorForm
{
    internal static class Utilities
    {
        public static int CountLines(string text, int startIndex, int endIndex)
        {
            var linesCount = 0;
            for (; startIndex < endIndex; startIndex++)
            {
                if (text[startIndex] != '\r' && text[startIndex] != '\n')
                    continue;

                linesCount++;
                if (text[startIndex] != text[startIndex + 1] &&
                    (text[startIndex + 1] == '\r' || text[startIndex + 1] == '\n'))
                    startIndex++;
            }

            return linesCount;
        }

        public static bool FindTextLines(string text, string sample, out int startLine, out int lineNum)
        {
            startLine = 0;
            lineNum = 0;
            var compactText = Utilities.TrimJson(text, true);
            var compactSample = Utilities.TrimJson(sample, true);
            var startIndex = compactText.IndexOf(compactSample, StringComparison.Ordinal);
            if (startIndex < 0)
                return false;

            startLine = Utilities.CountLines(compactText, 0, startIndex);
            lineNum = Utilities.CountLines(compactText, startIndex, startIndex + compactSample.Length);

            return true;
        }

        public static string TrimJson(string original, bool trimEol)
        {
            if (string.IsNullOrEmpty(original))
                return original;

            original = original.Trim();
            if (string.IsNullOrEmpty(original))
                return original;

            if (trimEol)
            {
                original = original.Replace("\r\n", "\n");
                original = original.Replace('\r', '\n');
            }

            var i = original.IndexOf("\n ", StringComparison.Ordinal);
            while (i >= 0)
            {
                original = original.Replace("\n ", "\n");
                i = original.IndexOf("\n ", i, StringComparison.Ordinal);
            }

            if (trimEol)
                return original;

            i = original.IndexOf("\r ", StringComparison.Ordinal);
            while (i >= 0)
            {
                original = original.Replace("\r ", "\r");
                i = original.IndexOf("\r ", i, StringComparison.Ordinal);
            }

            return original;
        }

        public static string BeautifyJson(string json, bool singleLineBrackets)
        {
            if (string.IsNullOrEmpty(json))
                return json;

            json = json.Trim();
            json = ReformatJson(json, true);

            return singleLineBrackets ? JsonShiftBrackets_v2(json) : json;
        }

        public static string ReformatJson(string json, bool formatted)
        {
            if (json.Contains(':') && (json.StartsWith("{") && json.EndsWith("}") ||
                                       json.StartsWith("[") && json.EndsWith("]")))
            {
                try
                {
                    using (var stringReader = new StringReader(json))
                    {
                        using (var stringWriter = new StringWriter())
                        {
                            using (var jsonReader = new JsonTextReader(stringReader))
                            {
                                using (var jsonWriter = new JsonTextWriter(stringWriter)
                                {
                                    Formatting = formatted ? Formatting.Indented : Formatting.None
                                })
                                {
                                    jsonWriter.WriteToken(jsonReader);
                                    return stringWriter.ToString();
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return json;
        }

        // definitely need rework
        public static string JsonShiftBrackets_v2(string original)
        {
            if (string.IsNullOrEmpty(original))
                return original;

            var searchTokens = new[] { ": {", ": [", ":{", ":[" };
            try
            {
                foreach (var token in searchTokens)
                {
                    var i = original.IndexOf(token, StringComparison.Ordinal);
                    while (i >= 0)
                    {
                        int currentPos;
                        // not a single bracket
                        if (original[i + token.Length] != '\r' && original[i + token.Length] != '\n')
                        {
                            currentPos = i + token.Length;
                        }
                        // need to shift bracket down the line
                        else
                        {
                            var j = i - 1;
                            var trail = 0;

                            if (j >= 0)
                            {
                                while (original[j] != '\n' && original[j] != '\r' && j >= 0)
                                {
                                    if (original[j] == ' ')
                                        trail++;
                                    else
                                        trail = 0;

                                    j--;
                                }
                            }

                            if (j < 0)
                                j = 0;

                            if (!(original[j] == '/' && original[j + 1] == '/')) // if it's a comment
                            {
                                original = original.Insert(i + 2, Environment.NewLine + new string(' ', trail));
                            }

                            currentPos = i + token.Length;
                        }

                        i = original.IndexOf(token, currentPos, StringComparison.Ordinal);
                    }
                }
            }
            catch
            {
                return original;
            }

            var stringList = ConvertTextToStringList(original);

            const char prefixItem = ' ';
            const int prefixStep = 2;
            var openBrackets = new[] { '{', '[' };
            var closeBrackets = new[] { '}', ']' };

            var prefixLength = 0;
            var prefix = "";
            var result = new StringBuilder();

            try
            {
                for (var i = 0; i < stringList.Length; i++)
                {
                    stringList[i] = stringList[i].Trim();
                    if (closeBrackets.Contains(stringList[i][0]))
                    {
                        prefixLength -= prefixStep;
                        if (prefixLength >= 0)
                            prefix = new string(prefixItem, prefixLength);
                    }

                    result.AppendLine(prefix + stringList[i]);

                    if (openBrackets.Contains(stringList[i][0]))
                    {
                        prefixLength += prefixStep;
                        if (stringList[i].Length > 1 && closeBrackets.Contains(stringList[i][stringList[i].Length - 1]))
                            prefixLength -= prefixStep;

                        if (prefixLength >= 0)
                            prefix = new string(prefixItem, prefixLength);
                    }
                }
            }
            catch
            {
                return original;
            }

            return result.ToString().Trim();
        }

        public static string[] ConvertTextToStringList(string data)
        {
            var stringCollection = new List<string>();
            if (string.IsNullOrEmpty(data))
                return stringCollection.ToArray();

            var lineDivider = new List<char> { '\x0d', '\x0a' };
            var unparsedData = "";
            foreach (var t in data)
            {
                if (lineDivider.Contains(t))
                {
                    if (unparsedData.Length > 0)
                    {
                        stringCollection.Add(unparsedData);
                        unparsedData = "";
                    }
                }
                else
                {
                    unparsedData += t;
                }
            }

            if (unparsedData.Length > 0)
                stringCollection.Add(unparsedData);

            return stringCollection.ToArray();
        }
    }
}
