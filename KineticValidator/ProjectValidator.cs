// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++, C#, and Java: http://www.viva64.com

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using JsonPathParserLib;

using NJsonSchema;
using NJsonSchema.Validation;

namespace KineticValidator
{
    public static class ProjectValidator
    {
        //constants from caller
        private static char _splitChar;
        private static string _schemaTag;
        private static string _backupSchemaExtension;
        private static string _fileMask;
        private static string _serverAssembliesPath;
        private static IEnumerable<ContentTypeItem> _fileTypes;

        //setting flags from UI
        private static bool _ignoreHttpsError;
        private static bool _skipSchemaErrors;
        private static bool _patchAllFields;

        //settings from .config
        private static string[] _systemMacros;
        private static string[] _systemDataViews;
        private static IEnumerable<ValidationErrorKind> _suppressSchemaErrors;

        //project folder related settings
        private static string _projectName;
        private static string _projectPath;
        private static FolderType _folderType;

        //only set
        private static ConcurrentDictionary<string, string> _processedFilesList;
        private static IEnumerable<JsonProperty> _jsonPropertiesCollection;
        private static IEnumerable<ReportItem> _runValidationReportsCollection;
        private static IEnumerable<ReportItem> _deserializeFileReportsCollection;
        private static IEnumerable<ReportItem> _parseJsonObjectReportsCollection;

        internal static Dictionary<string, Func<string, IEnumerable<ReportItem>>> ValidatorsList =>
            new Dictionary<string, Func<string, IEnumerable<ReportItem>>>
            {
                                {"File list validation", RunValidation},
                {"Serialization validation", DeserializeFile},
                {"JSON parser validation", ParseJsonObject},
                {"Schema validation", SchemaValidation},
                {"Redundant files", RedundantFiles},
                {"National characters", ValidateFileChars},
                {"Duplicate JSON property names", DuplicateIds},
                {"Empty patch names", EmptyPatchNames},
                {"Redundant patches", RedundantPatches},
                {"Non existing patches", CallNonExistingPatches},
                {"Overriding patches", OverridingPatches},
                {"Possible patches", PossiblePatchValues},
                {"Hard-coded message strings", HardCodedStrings},
                {"Possible strings", PossibleStringsValues},
                {"Apply patches", PatchAllFields},
                {"Empty string names", EmptyStringNames},
                {"Empty string values", EmptyStringValues},
                {"Redundant strings", RedundantStrings},
                {"Non existing strings", CallNonExistingStrings},
                {"Overriding strings", OverridingStrings},
                {"Empty event names", EmptyEventNames},
                {"Empty events", EmptyEvents},
                {"Overriding events", OverridingEvents},
                {"Redundant events", RedundantEvents},
                {"Non existing events", CallNonExistingEvents},
                {"Empty dataView names", EmptyDataViewNames},
                {"Redundant dataViews", RedundantDataViews},
                {"Non existing dataViews", CallNonExistingDataViews},
                {"Overriding dataViews", OverridingDataViews},
                {"Non existing dataTables", CallNonExistingDataTables},
                {"Empty rule names", EmptyRuleNames},
                {"Overriding rules", OverridingRules},
                {"Empty tool names", EmptyToolNames},
                {"Overriding tools", OverridingTools},
                {"Missing forms", MissingForms},
                {"Missing searches", MissingSearches},
                {"JavaScript code", JsCode},
                {"JS #_trans.dataView('DataView').count_#", JsDataViewCount},
                {"Missing layout id's", MissingLayoutIds},
                {"Incorrect layout id's", IncorrectLayoutIds},
                {"Incorrect dataview-condition", IncorrectDvConditionViewName},
                {"Incorrect tab links", IncorrectTabIds},
                {"Incorrect REST calls", IncorrectRestCalls},
                {"Duplicate GUID's", DuplicateGuiDs}
            };

        //cached data
        private static Dictionary<string, string> _patchValues;
        private static ConcurrentDictionary<string, List<ParsedProperty>> _parsedFiles = new ConcurrentDictionary<string, List<ParsedProperty>>();
        private static volatile bool _allFieldsPatched;

        //global cache
        private static readonly ConcurrentDictionary<string, string> SchemaList =
            new ConcurrentDictionary<string, string>();

        private static readonly ConcurrentDictionary<string, Dictionary<string, string[]>> KnownServices =
            new ConcurrentDictionary<string, Dictionary<string, string[]>>();

        private static readonly ConcurrentDictionary<string, Dictionary<string, Dictionary<string, string[]>>>
            KnownDataSets = new ConcurrentDictionary<string, Dictionary<string, Dictionary<string, string[]>>>();

        internal static void Initialize(ProcessConfiguration processConfiguration,
            ProjectConfiguration projectConfiguration,
            SeedData seedData,
            bool clearCache)
        {
            _splitChar = processConfiguration.SplitChar;
            _schemaTag = processConfiguration.SchemaTag;
            _backupSchemaExtension = processConfiguration.BackupSchemaExtension;
            _fileMask = processConfiguration.FileMask;
            _fileTypes = processConfiguration.FileTypes;
            _ignoreHttpsError = processConfiguration.IgnoreHttpsError;
            _skipSchemaErrors = processConfiguration.SkipSchemaErrors;
            _patchAllFields = processConfiguration.PatchAllFields;
            _systemMacros = processConfiguration.SystemMacros;
            _systemDataViews = processConfiguration.SystemDataViews;
            _suppressSchemaErrors = processConfiguration.SuppressSchemaErrors;
            _serverAssembliesPath = processConfiguration.ServerAssembliesPath;

            if (projectConfiguration != null)
            {
                _projectName = projectConfiguration.ProjectName;
                _projectPath = projectConfiguration.ProjectPath;
                _folderType = projectConfiguration.FolderType;
            }

            if (seedData != null)
            {
                //only set
                _processedFilesList = seedData.ProcessedFilesList;
                _jsonPropertiesCollection = seedData.JsonPropertiesCollection;
                _runValidationReportsCollection = seedData.RunValidationReportsCollection;
                _deserializeFileReportsCollection = seedData.DeserializeFileReportsCollection;
                _parseJsonObjectReportsCollection = seedData.ParseJsonObjectReportsCollection;
            }

            _patchValues = new Dictionary<string, string>();
            _allFieldsPatched = false;

            if (clearCache)
                _parsedFiles = new ConcurrentDictionary<string, List<ParsedProperty>>();
            //SchemaList = new Dictionary<string, string>();
            //KnownServices = new Dictionary<string, Dictionary<string, List<string>>>();
            //KnownDataSets = new ConcurrentDictionary<string, Dictionary<string, Dictionary<string, string[]>>>();
        }

        #region Helping methods

        private struct Brackets
        {
            public int Pos;
            public int Level;
            public char Bracket;
            public int Number;
        }

        private static List<string> GetTableField(string field)
        {
            var valueList = new List<string>();
            if (string.IsNullOrEmpty(field))
                return valueList;

            const char startChar = '{';
            const char endChar = '}';
            var improperChars = new[] { ' ', ',', ':', ';', '\'', '\"', '(', ')', '+', '=', '[', ']', '&', '|', '~' };

            var tokens = field.Split(improperChars);
            foreach (var token in tokens)
            {
                var startCharNum = CountChars(token, startChar);
                var endCharNum = CountChars(token, endChar);
                if (startCharNum != 0 && endCharNum != 0 && startCharNum == endCharNum)
                {
                    var l = 0;
                    var n = 0;
                    var sequence = new List<Brackets>();
                    for (var i = 0; i < token.Length; i++)
                        if (token[i] == startChar)
                        {
                            l++;
                            sequence.Add(new Brackets
                            {
                                Pos = i,
                                Level = l,
                                Bracket = startChar,
                                Number = n
                            });
                            n++;
                        }
                        else if (token[i] == endChar)
                        {
                            sequence.Add(new Brackets
                            {
                                Pos = i,
                                Level = l,
                                Bracket = endChar,
                                Number = n
                            });
                            n++;
                            l--;
                        }

                    l = sequence.Max(brackets => brackets.Level);
                    for (; l > 0; l--)
                    {
                        var s = sequence.Where(m => m.Level == l).ToArray();
                        for (var i = 1; i < s.Length; i++)
                            if (s[i - 1].Number + 1 == s[i].Number && s[i - 1].Bracket == startChar &&
                                s[i].Bracket == endChar)
                            {
                                var str = token.Substring(s[i - 1].Pos + 1, s[i].Pos - s[i - 1].Pos - 1);
                                if (str.Contains('.'))
                                    valueList.Add(str);
                            }
                    }
                }
            }

            return valueList;
        }

        private static int CountChars(string data, char countChar)
        {
            return data.Count(ch => ch == countChar);
        }

        private static IEnumerable<string> GetPatchList(string field)
        {
            var patchList = new List<string>();
            if (string.IsNullOrEmpty(field))
                return patchList;

            var improperChars = new[] { ' ', '.', ',', ':', ';', '\'', '\"', '(', ')', '{', '}', '[', ']', '~' };
            var tokens = field.Split(improperChars);
            const char tokenEmbracementChar = '%';
            foreach (var token in tokens)
            {
                var c = CountChars(field, tokenEmbracementChar);
                if (c != 0 && c % 2 == 0)
                {
                    var pos1 = 0;
                    do
                    {
                        pos1 = token.IndexOf(tokenEmbracementChar, pos1);
                        if (pos1 >= 0 && pos1 < token.Length - 2)
                        {
                            var pos2 = token.LastIndexOf(tokenEmbracementChar);

                            if (pos2 >= 0 && pos2 - pos1 > 1)
                            {
                                var newPatch = token.Substring(pos1, pos2 - pos1 + 1);
                                if (newPatch.IndexOf(tokenEmbracementChar, 1, newPatch.Length - 2) >= 0)
                                {
                                    // stack overflow comes on "%%patch%" processing. Easy way to handle.
                                    if (newPatch.StartsWith("%%") && newPatch.EndsWith("%"))
                                        pos1 = pos2 + 1;
                                    else
                                        patchList.AddRange(GetPatchList(newPatch));
                                }
                                else
                                {
                                    patchList.Add(newPatch);
                                    pos1 = pos2 + 1;
                                }
                            }
                            else
                            {
                                pos1++;
                            }
                        }
                    } while (pos1 >= 0 && pos1 < token.Length - 2);
                }
            }

            return patchList;
        }

        private static string GetParentName(string parentPath)
        {
            var i = parentPath.LastIndexOf('.');
            return parentPath.Substring(i + 1);
        }

        private static bool IsTableField(string field)
        {
            var regex = new Regex(@"^{+\w+[.]+\w+}$");
            return regex.Match(field.Trim('\'')).Success;
        }

        private static bool IsPatch(string field)
        {
            var regex = new Regex(@"^%+\w+%$");
            return regex.Match(field.Trim('\'')).Success;
        }

        private static bool HasJsCode(string field)
        {
            return field.Contains("#_") && field.Contains("_#");
        }

        private static bool HasJsDvCount(string field)
        {
            //"#_*trans.dataView(*).count*_#"
            //Regex regex = new Regex(@"^#_+[\s\S\w]+rans.dataView\(+[\s\w]+\).count+[\s\S\w]_#$");
            //return regex.Match(field).Success;
            return field.Contains("trans.dataView(") && field.Contains(").count");
        }

        private static readonly object LockSchemaGetter = new object();

        private static async Task<List<ReportItem>> ValidateFileSchema(string projectName,
            string fullFileName,
            string schemaUrl = "")
        {
            var report = new List<ReportItem>();
            string jsonText;
            try
            {
                jsonText = File.ReadAllText(fullFileName);
            }
            catch (Exception ex)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = projectName,
                    FullFileName = fullFileName,
                    Message = "File read exception: " + Utilities.ExceptionPrint(ex),
                    ValidationType = ValidationTypeEnum.File.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "ValidateFileSchema"
                };
                report.Add(reportItem);
                return report;
            }

            if (string.IsNullOrEmpty(schemaUrl))
            {
                var versionIndex = jsonText.IndexOf(_schemaTag, StringComparison.Ordinal);
                if (versionIndex <= 0)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = projectName,
                        FullFileName = fullFileName,
                        Message = "Schema property not found",
                        ValidationType = ValidationTypeEnum.Scheme.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "ValidateFileSchema"
                    };
                    report.Add(reportItem);
                    return report;
                }

                versionIndex += _schemaTag.Length;
                while (versionIndex < jsonText.Length
                       && jsonText[versionIndex] != '"'
                       && jsonText[versionIndex] != '\r'
                       && jsonText[versionIndex] != '\n')
                    versionIndex++;

                versionIndex++;
                var strEnd = versionIndex;
                while (strEnd < jsonText.Length
                       && jsonText[strEnd] != '"'
                       && jsonText[strEnd] != '\r'
                       && jsonText[strEnd] != '\n')
                    strEnd++;

                if (versionIndex >= 0 && jsonText.Length > versionIndex)
                    schemaUrl = jsonText.Substring(versionIndex, strEnd - versionIndex).Trim();
            }

            if (string.IsNullOrEmpty(schemaUrl) || !schemaUrl.EndsWith(".json"))
            {
                var reportItem = new ReportItem
                {
                    ProjectName = projectName,
                    FullFileName = fullFileName,
                    Message = $"URL incorrect [{schemaUrl}]",
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "ValidateFileSchema"
                };
                report.Add(reportItem);

                return report;
            }

            lock (LockSchemaGetter)
            {
                if (!SchemaList.ContainsKey(schemaUrl))
                {
                    var schemaData = "";
                    try
                    {
                        schemaData = GetSchemaText(schemaUrl);
                    }
                    catch (Exception ex)
                    {
                        var reportItem = new ReportItem
                        {
                            ProjectName = projectName,
                            FullFileName = fullFileName,
                            Message = $"Schema download exception [{schemaUrl}]: {Utilities.ExceptionPrint(ex)}",
                            ValidationType = ValidationTypeEnum.Scheme.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "ValidateFileSchema"
                        };
                        report.Add(reportItem);
                    }

                    SchemaList.TryAdd(schemaUrl, schemaData);
                }
            }

            var schemaText = SchemaList[schemaUrl];

            if (string.IsNullOrEmpty(schemaText))
            {
                var reportItem = new ReportItem
                {
                    ProjectName = projectName,
                    FullFileName = fullFileName,
                    Message = $"Schema is empty [{schemaUrl}]",
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "ValidateFileSchema"
                };
                report.Add(reportItem);
                return report;
            }

            JsonSchema schema;
            try
            {
                schema = await JsonSchema.FromJsonAsync(schemaText).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = projectName,
                    FullFileName = fullFileName,
                    Message = $"Schema parse exception [{schemaUrl}]: {Utilities.ExceptionPrint(ex)}",
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "ValidateFileSchema"
                };
                report.Add(reportItem);

                return report;
            }

            if (schema == null)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = projectName,
                    FullFileName = fullFileName,
                    Message = $"Schema is empty [{schemaUrl}]",
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "ValidateFileSchema"
                };
                report.Add(reportItem);

                return report;
            }

            ICollection<ValidationError> errors;
            try
            {
                errors = schema.Validate(jsonText);
            }
            catch (Exception ex)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = projectName,
                    FullFileName = fullFileName,
                    Message = "File validation exception: " + Utilities.ExceptionPrint(ex),
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "ValidateFileSchema"
                };
                report.Add(reportItem);

                return report;
            }

            foreach (var error in errors)
            {
                //var errorText = SchemaErrorToString(fullFileName, error);
                var errorList = GetErrorList(projectName, error);
                if (_skipSchemaErrors && _suppressSchemaErrors.Contains(error.Kind))
                {
                    //Utilities.SaveDevLog(errorText);
                }
                else
                {
                    var newReportList = errorList?.Select(schemaError => new ReportItem
                    {
                        ProjectName = projectName,
                        FullFileName = fullFileName,
                        FileType = Utilities.GetFileTypeFromFileName(fullFileName, _fileTypes).ToString(),
                        LineId = schemaError.LineId,
                        JsonPath = schemaError.JsonPath.TrimStart('#', '/'),
                        Message = schemaError.Message,
                        ValidationType = ValidationTypeEnum.Scheme.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "ValidateFileSchema"
                    }).ToList();
                    if (newReportList != null && newReportList.Any())
                        report.AddRange(newReportList);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> GetErrorList(string projectName, ValidationError error)
        {
            var errorList = new List<ReportItem>();
            if (error is ChildSchemaValidationError subErrorCollection)
            {
                errorList.AddRange(from subError in subErrorCollection.Errors
                                   from subErrorItem in subError.Value
                                   select new ReportItem
                                   {
                                       ProjectName = projectName,
                                       LineId = subErrorItem.LineNumber.ToString(),
                                       JsonPath = subErrorItem.Path.TrimStart('#', '/'),
                                       Message = subErrorItem.Kind.ToString(),
                                       ValidationType = ValidationTypeEnum.Scheme.ToString(),
                                       Severity = ImportanceEnum.Error.ToString(),
                                       Source = "GetErrorList"
                                   });
            }
            else
            {
                var report = new ReportItem
                {
                    ProjectName = projectName,
                    JsonPath = error.Path.TrimStart('#', '/'),
                    Message = error.Kind.ToString(),
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "GetErrorList"
                };
                errorList.Add(report);
            }

            return errorList;
        }

        private static string SchemaErrorToString(string fullFileName, ValidationError error)
        {
            var errorText = new StringBuilder();
            errorText.AppendLine(fullFileName
                                 + ": line #"
                                 + error.LineNumber
                                 + " "
                                 + error.Kind
                                 + ", path="
                                 + error.Path);

            if (error is ChildSchemaValidationError subErrorCollection)
                foreach (var subError in subErrorCollection.Errors)
                    foreach (var subErrorItem in subError.Value)
                        errorText.AppendLine("\t"
                                             + "- line #"
                                             + subErrorItem.LineNumber
                                             + " "
                                             + subErrorItem.Kind
                                             + ", path="
                                             + subErrorItem.Path);

            return errorText.ToString();
        }

        private static string GetSchemaText(string schemaUrl)
        {
            var schemaData = "";
            if (string.IsNullOrEmpty(schemaUrl))
                return schemaData;

            var localPath = GetLocalUrlPath(schemaUrl);
            if (File.Exists(localPath))
            {
                schemaData = File.ReadAllText(localPath);
            }
            else
            {
                if (_ignoreHttpsError)
                    ServicePointManager.ServerCertificateValidationCallback = (a, b, c, d) => true;
                using (var webClient = new WebClient())
                {
                    schemaData = webClient.DownloadString(schemaUrl);
                    var dirPath = Path.GetDirectoryName(localPath);
                    if (dirPath != null)
                        try
                        {
                            if (!Directory.Exists(dirPath))
                                Directory.CreateDirectory(dirPath);

                            File.WriteAllText(localPath + _backupSchemaExtension, schemaData);
                        }
                        catch (Exception ex)
                        {
                            Utilities.SaveDevLog(ex.Message);
                            return schemaData;
                        }
                }
            }

            return schemaData;
        }

        private static string GetLocalUrlPath(string url)
        {
            if (!url.Contains("://"))
                return "";

            url = url.Replace("://", "");
            if (!url.Contains("/"))
                return "";

            var currentDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
            var i = url.IndexOf('/');
            if (i < 0 || url.Length <= url.IndexOf('/'))
                return "";

            var localPath = currentDirectory + url.Substring(i);
            localPath = localPath.Replace('/', '\\');
            return localPath;
        }

        private static IEnumerable<ParsedProperty> GetParsedFile(string file)
        {
            var list = new List<ParsedProperty>();
            if (!_parsedFiles.ContainsKey(file))
            {
                string json;
                try
                {
                    json = File.ReadAllText(file);
                }
                catch (Exception ex)
                {
                    Utilities.SaveDevLog(ex.Message);
                    return list;
                }

                var parcer = new JsonPathParser
                {
                    TrimComplexValues = false,
                    SaveAllValues = false,
                    RootName = "",
                    JsonPathDivider = '.',
                };

                list = parcer.ParseJsonToPathList(json.Replace(' ', ' '), out var _, out var _).ToList();
                _parsedFiles.TryAdd(file, list);
            }
            else
            {
                list = _parsedFiles[file];
            }

            return list;
        }

        private struct RestMethodInfo
        {
            public string SvcName;
            public string MethodName;
            public string[] Parameters;
        }

        private static RestMethodInfo GetRestCallParams(string svcName, string methodName, string assemblyPath)
        {
            var result = new RestMethodInfo();

            if (KnownServices.ContainsKey(svcName))
            {
                result.SvcName = svcName;
                KnownServices.TryGetValue(svcName, out var methods);
                if (methods != null && methods.ContainsKey(methodName))
                {
                    result.MethodName = methodName;
                    methods.TryGetValue(methodName, out result.Parameters);
                }
            }
            else
            {
                var domain = AppDomain.CreateDomain(nameof(AssemblyLoader), AppDomain.CurrentDomain.Evidence,
                    new AppDomainSetup
                    {
                        ApplicationBase = Path.GetDirectoryName(typeof(AssemblyLoader).Assembly.Location)
                        //PrivateBinPath = assemblyPath + "\\..\\Bin"
                    });
                try
                {
                    var loader =
                        (AssemblyLoader)domain.CreateInstanceAndUnwrap(typeof(AssemblyLoader).Assembly.FullName,
                            typeof(AssemblyLoader).FullName ?? string.Empty);

                    var assemblyLoadResult = loader.LoadAssembly(svcName, assemblyPath);

                    if (!assemblyLoadResult)
                        return result;

                    result.SvcName = svcName;
                    KnownServices.TryAdd(svcName, loader.AllMethodsInfo);
                    if (loader.AllMethodsInfo != null && loader.AllMethodsInfo.ContainsKey(methodName))
                    {
                        result.MethodName = methodName;
                        loader.AllMethodsInfo.TryGetValue(methodName, out result.Parameters);
                    }
                }
                catch (Exception ex)
                {
                    Utilities.SaveDevLog(ex.Message);
                }
                finally
                {
                    AppDomain.Unload(domain);
                }
            }

            return result;
        }

        private struct RestDataSetInfo
        {
            public string SvcName;
            public string DataSetName;
            public string TableName;
            public string[] Fields;
        }

        private static RestDataSetInfo GetRestServiceDataSets(string svcName, string dataSetName, string tableName,
            string assemblyPath)
        {
            var result = new RestDataSetInfo();

            if (KnownDataSets.ContainsKey(svcName))
            {
                result.SvcName = svcName;
                KnownDataSets.TryGetValue(svcName, out var dataSets);
                if (dataSets != null && dataSets.ContainsKey(dataSetName))
                {
                    result.DataSetName = dataSetName;
                    Dictionary<string, string[]> tables = null;
                    dataSets?.TryGetValue(svcName, out tables);

                    if (tables != null && tables.ContainsKey(tableName))
                    {
                        result.TableName = tableName;
                        tables.TryGetValue(tableName, out result.Fields);
                    }
                }
            }
            else
            {
                var domain = AppDomain.CreateDomain(nameof(AssemblyLoader), AppDomain.CurrentDomain.Evidence,
                    new AppDomainSetup
                    {
                        ApplicationBase = Path.GetDirectoryName(typeof(AssemblyLoader).Assembly.Location)
                        //PrivateBinPath = assemblyPath + "\\..\\Bin"
                    });
                try
                {
                    var loader =
                        (AssemblyLoader)domain.CreateInstanceAndUnwrap(typeof(AssemblyLoader).Assembly.FullName,
                            typeof(AssemblyLoader).FullName ?? string.Empty);

                    var assemblyLoadResult = loader.LoadAssembly(svcName, assemblyPath);

                    if (!assemblyLoadResult)
                        return result;

                    result.SvcName = svcName;
                    KnownDataSets.TryAdd(svcName, loader.AllDataSetsInfo);
                }
                catch (Exception ex)
                {
                    Utilities.SaveDevLog(ex.Message);
                }
                finally
                {
                    AppDomain.Unload(domain);
                }
            }

            return result;
        }

        private static Dictionary<string, string> CollectPatchValues()
        {
            var patchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Patch
                    && n.Parent == "patch"
                    && n.Name == "id");

            var patchValues = new Dictionary<string, string>();
            foreach (var item in patchList)
            {
                var newValue = _jsonPropertiesCollection
                    .LastOrDefault(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.Patch
                        && n.FullFileName == item.FullFileName
                        && n.ParentPath == item.ParentPath
                        && n.Name == "value")
                    ?.Value;

                var patchName = "%" + item.Value + "%";
                if (!patchValues.ContainsKey(patchName))
                    patchValues.Add(patchName, newValue);
                else
                    patchValues[patchName] = newValue;
            }

            var allReplaced = false;
            do
            {
                var toPatch = patchValues.Where(n =>
                    n.Value.Trim().StartsWith("%")
                    && n.Value.Trim().EndsWith("%")).ToArray();

                allReplaced = true;

                if (toPatch.Any())
                {
                    var replaceValues = new Dictionary<string, string>();
                    foreach (var item in toPatch)
                        foreach (var patch in patchValues.Where(patch => patch.Key == item.Value))
                        {
                            replaceValues.Add(item.Key, patch.Value);
                            allReplaced = false;
                            break;
                        }

                    foreach (var r in replaceValues)
                        patchValues[r.Key] = r.Value;
                }
            } while (!allReplaced);

            return patchValues;
        }

        #endregion

        #region Validator methods

        private static IEnumerable<ReportItem> RunValidation(string methodName)
        {
            return _runValidationReportsCollection;
        }

        private static IEnumerable<ReportItem> DeserializeFile(string methodName)
        {
            return _deserializeFileReportsCollection;
        }

        private static IEnumerable<ReportItem> ParseJsonObject(string methodName)
        {
            return _parseJsonObjectReportsCollection;
        }

        private static IEnumerable<ReportItem> SchemaValidation(string methodName)
        {
            // validate every file with schema
            var report = new BlockingCollection<ReportItem>();
            Parallel.ForEach(_processedFilesList, file =>
            {
                var reportSet = ValidateFileSchema(_projectName, file.Key, file.Value).Result;

                foreach (var item in reportSet)
                    report.Add(item);
            });

            return report;
        }

        private static IEnumerable<ReportItem> RedundantFiles(string methodName)
        {
            // collect full list of files inside the project folder (not including shared)
            var fullFilesList = new List<string>();
            var foundFiles = Directory.GetFiles(_projectPath, _fileMask, SearchOption.AllDirectories);
            fullFilesList.AddRange(foundFiles);

            var report = (from file in fullFilesList
                          where file.IndexOf(_projectPath + "\\views\\", StringComparison.OrdinalIgnoreCase) < 0
                          where !_processedFilesList.ContainsKey(file) && !Utilities.IsShared(file, _projectPath)
                          select new ReportItem
                          {
                              ProjectName = _projectName,
                              FullFileName = file,
                              Message = "File is not used in the project",
                              ValidationType = ValidationTypeEnum.Logic.ToString(),
                              Severity = ImportanceEnum.Note.ToString(),
                              Source = methodName
                          }).ToList();

            return report;
        }

        private static IEnumerable<ReportItem> ValidateFileChars(string methodName)
        {
            var report = new List<ReportItem>();

            var propertyCollection = _jsonPropertiesCollection.Where(n =>
                !n.Shared
                && n.ItemType == JsonItemType.Property
                && !string.IsNullOrEmpty(n.Name)
                && !string.IsNullOrEmpty(n.Value));

            foreach (var item in propertyCollection)
            {
                var charNum = 1;
                var chars = new List<int>();
                foreach (var ch in item.Name)
                {
                    if (ch > 127)
                        chars.Add(charNum);

                    charNum++;
                }

                if (chars.Count > 0)
                {
                    var charPos = new StringBuilder();
                    foreach (var ch in chars)
                        if (charPos.Length > 0)
                            charPos.Append(", " + ch);
                        else
                            charPos.Append(ch);

                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        LineNumber = item.SourceLineNumber.ToString(),
                        Message = $"Json property \"{item.Name}\" has non-ASCII chars at position(s) {charPos}",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }

                charNum = 1;
                chars = new List<int>();
                foreach (var ch in item.Value)
                {
                    if (ch > 127)
                        chars.Add(charNum);

                    charNum++;
                }

                if (chars.Count > 0)
                {
                    var charPos = new StringBuilder();
                    foreach (var ch in chars)
                        if (charPos.Length > 0)
                            charPos.Append(", " + ch);
                        else
                            charPos.Append(ch);

                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        LineNumber = item.SourceLineNumber.ToString(),
                        Message = $"JSON property value \"{item.Value}\" has non-ASCII chars at position(s) {charPos}",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> DuplicateIds(string methodName)
        {
            var report = new List<ReportItem>();

            foreach (var file in _processedFilesList)
            {
                var propertyList = GetParsedFile(file.Key);

                var duplicateIdList = propertyList.Where(n =>
                        n.PropertyType == PropertyType.Property)
                    .GroupBy(n => n.Path)
                    .Where(n => n.Count() > 1).ToArray();

                if (!duplicateIdList.Any())
                    continue;

                foreach (var dup in duplicateIdList)
                {
                    var names = "";
                    foreach (var item in dup)
                    {
                        if (string.IsNullOrEmpty(names))
                            names += item.Value;
                        else
                            names += ", " + item.Value;
                    }

                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = file.Key + _splitChar.ToString()
                        + file.Key,
                        JsonPath = "[1]" + dup.First().Path
                        + _splitChar.ToString()
                        + "[2]" + dup.Last().Path,
                        StartPosition = dup.First().StartPosition + _splitChar.ToString()
                        + dup.Last().StartPosition,
                        EndPosition = dup.First().EndPosition + _splitChar.ToString()
                        + dup.Last().EndPosition,
                        Message =
                            $"JSON file has duplicate property names [{names}]",
                        ValidationType = ValidationTypeEnum.Parse.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName,
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> EmptyPatchNames(string methodName)
        {
            var emptyPatchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Patch
                    && !n.Shared
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && string.IsNullOrEmpty(n.Value));

            return emptyPatchList.Select(patchResource => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = patchResource.FullFileName,
                FileType = patchResource.FileType.ToString(),
                LineId = patchResource.LineId.ToString(),
                JsonPath = patchResource.JsonPath,
                LineNumber = patchResource.SourceLineNumber.ToString(),
                Message = $"Patch id \"{patchResource.Value}\" is empty",
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Error.ToString(),
                Source = methodName
            });
        }

        private static IEnumerable<ReportItem> RedundantPatches(string methodName)
        {
            var patchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Patch
                    && !n.Shared
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value));

            return from patchResource in patchList
                   where !string.IsNullOrEmpty(patchResource.Value)
                   where !_jsonPropertiesCollection.Any(n =>
                       n.ItemType == JsonItemType.Property && n.FileType != KineticContentType.Patch &&
                       n.Value.Contains("%" + patchResource.Value + "%"))
                   select new ReportItem
                   {
                       ProjectName = _projectName,
                       FullFileName = patchResource.FullFileName,
                       FileType = patchResource.FileType.ToString(),
                       LineId = patchResource.LineId.ToString(),
                       JsonPath = patchResource.JsonPath,
                       LineNumber = patchResource.SourceLineNumber.ToString(),
                       Message = $"Patch \"{patchResource.Value}\" is not used in the project",
                       ValidationType = ValidationTypeEnum.Logic.ToString(),
                       Severity = ImportanceEnum.Warning.ToString(),
                       Source = methodName
                   };
        }

        private static IEnumerable<ReportItem> CallNonExistingPatches(string methodName)
        {
            var patchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Patch
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value));

            return from property in _jsonPropertiesCollection
                   where property.ItemType == JsonItemType.Property
                   let usedPatchList = GetPatchList(property.Value)
                   from patchItem in usedPatchList
                   where !string.IsNullOrEmpty(patchItem)
                   where !_systemMacros.Contains(patchItem) && patchList.All(n => n.Value != patchItem.Trim('%'))
                   select new ReportItem
                   {
                       ProjectName = _projectName,
                       FullFileName = property.FullFileName,
                       FileType = property.FileType.ToString(),
                       LineId = property.LineId.ToString(),
                       JsonPath = property.JsonPath,
                       LineNumber = property.SourceLineNumber.ToString(),
                       Message = $"Patch \"{patchItem}\" is not defined in the project",
                       ValidationType = ValidationTypeEnum.Logic.ToString(),
                       Severity = ImportanceEnum.Error.ToString(),
                       Source = methodName
                   };
        }

        private static IEnumerable<ReportItem> OverridingPatches(string methodName)
        {
            var duplicatePatchesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Patch
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1)
                .ToArray();

            var report = new List<ReportItem>();
            if (!duplicatePatchesList.Any())
                return report;

            foreach (var dup in duplicatePatchesList)
            {
                var patchDup = dup.ToArray();
                for (var i = 1; i < patchDup.Count(); i++)
                {
                    var oldValue = _jsonPropertiesCollection
                        .LastOrDefault(n => n.FileType == KineticContentType.Patch
                                            && n.FullFileName == patchDup[i - 1].FullFileName
                                            && n.ParentPath == patchDup[i - 1].ParentPath
                                            && n.Name == "value");

                    if (oldValue == null || oldValue.Shared)
                        continue;

                    var newValue = _jsonPropertiesCollection
                        .LastOrDefault(n => n.FileType == KineticContentType.Patch
                                            && n.FullFileName == patchDup[i].FullFileName
                                            && n.ParentPath == patchDup[i].ParentPath
                                            && n.Name == "value");

                    if (newValue == null || string.IsNullOrEmpty(oldValue.Value) ||
                        oldValue.Value == newValue.Value)
                        continue;

                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = patchDup[i - 1].FullFileName
                                       + _splitChar
                                       + patchDup[i].FullFileName,
                        Message =
                            $"Patch \"{patchDup[i - 1].Value}\" is overridden{Environment.NewLine} [\"{oldValue.Value}\" => \"{newValue.Value}\"]",
                        FileType = patchDup[i - 1].FileType
                                   + _splitChar.ToString()
                                   + patchDup[i].FileType,
                        LineId = patchDup[i - 1].LineId
                                 + _splitChar.ToString()
                                 + patchDup[i].LineId,
                        JsonPath = patchDup[i - 1].JsonPath
                                   + _splitChar
                                   + patchDup[i].JsonPath,
                        LineNumber = patchDup[i - 1].SourceLineNumber.ToString()
                                     + _splitChar
                                     + patchDup[i].SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> PossiblePatchValues(string methodName)
        {
            if (_patchValues == null || _patchValues.Count == 0)
                _patchValues = CollectPatchValues();

            var missedPatchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType != KineticContentType.Patch
                    && _patchValues.Any(m =>
                        !string.IsNullOrEmpty(m.Value)
                        && m.Value == n.Value));

            var report = new List<ReportItem>();
            foreach (var item in missedPatchList)
            {
                var patchList = _patchValues
                    .Where(m => m.Value == item.Value)
                    .Select(n => n.Key);
                var patchDefinitions = "";
                foreach (var str in patchList)
                {
                    if (!string.IsNullOrEmpty(patchDefinitions))
                        patchDefinitions += ", ";

                    patchDefinitions += str;
                }

                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = item.FullFileName,
                    FileType = item.FileType.ToString(),
                    LineId = item.LineId.ToString(),
                    JsonPath = item.JsonPath,
                    LineNumber = item.SourceLineNumber.ToString(),
                    Message = $"Value \"{item.Value}\" can be replaced with patch(es): {patchDefinitions}",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }

            return report;
        }

        private static IEnumerable<ReportItem> HardCodedStrings(string methodName)
        {
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "message"
                    && !n.Value.Contains("{{strings.")
                    && !IsTableField(n.Value));

            return stringsList.Select(field => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = field.FullFileName,
                FileType = field.FileType.ToString(),
                LineId = field.LineId.ToString(),
                JsonPath = field.JsonPath,
                LineNumber = field.SourceLineNumber.ToString(),
                Message = $"String \"{field.Value}\" should be moved to strings.jsonc resource file",
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Note.ToString(),
                Source = methodName
            });
        }

        private static IEnumerable<ReportItem> PossibleStringsValues(string methodName)
        {
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.Value)
                    && !string.IsNullOrEmpty(n.Name));

            var missedStringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType != KineticContentType.Strings
                    && stringsList.Any(m =>
                        !string.IsNullOrEmpty(m.Value)
                        && m.Value == n.Value));

            var report = new List<ReportItem>();
            foreach (var item in missedStringsList)
            {
                var strList = stringsList
                    .Where(m => m.Value == item.Value)
                    .Select(n => n.Name);
                var stringDefinitions = "";

                foreach (var str in strList)
                {
                    if (!string.IsNullOrEmpty(stringDefinitions))
                        stringDefinitions += ", ";

                    stringDefinitions += str;
                }

                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = item.FullFileName,
                    FileType = item.FileType.ToString(),
                    LineId = item.LineId.ToString(),
                    JsonPath = item.JsonPath,
                    LineNumber = item.SourceLineNumber.ToString(),
                    Message = $"String \"{item.Value}\" can be replaced with string variable(s): {stringDefinitions}",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }

            return report;
        }

        // validations which works with patches applied
        private static IEnumerable<ReportItem> PatchAllFields(string methodName)
        {
            if (_patchValues == null || _patchValues.Count == 0)
                _patchValues = CollectPatchValues();

            var valuesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.Value.Contains('%'))
                .ToArray();

            if (valuesList.Any())
                foreach (var item in valuesList)
                    foreach (var patch in _patchValues)
                        item.PatchedValue = item.Value.Replace(patch.Key, patch.Value);

            _allFieldsPatched = true;

            return new List<ReportItem>();
        }

        private static IEnumerable<ReportItem> EmptyStringNames(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var emptyStringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Strings
                    && n.Parent == "strings"
                    && string.IsNullOrEmpty(n.Name));

            return emptyStringsList.Select(stringResource => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = stringResource.FullFileName,
                FileType = stringResource.FileType.ToString(),
                LineId = stringResource.LineId.ToString(),
                JsonPath = stringResource.JsonPath,
                LineNumber = stringResource.SourceLineNumber.ToString(),
                Message = $"String id \"{stringResource.Name}\" is empty",
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Error.ToString(),
                Source = methodName
            });
        }

        private static IEnumerable<ReportItem> EmptyStringValues(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var emptyStringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Strings
                    && n.Parent == "strings"
                    && string.IsNullOrEmpty(n.Value));

            return emptyStringsList.Select(stringResource => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = stringResource.FullFileName,
                FileType = stringResource.FileType.ToString(),
                LineId = stringResource.LineId.ToString(),
                JsonPath = stringResource.JsonPath,
                LineNumber = stringResource.SourceLineNumber.ToString(),
                Message = $"String id \"{stringResource.Name}\" value is empty",
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Warning.ToString(),
                Source = methodName
            });
        }

        private static IEnumerable<ReportItem> RedundantStrings(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.Value)
                    && !string.IsNullOrEmpty(n.Name));

            return from stringResource in stringsList
                   where !string.IsNullOrEmpty(stringResource.Name) && !_jsonPropertiesCollection.Any(n =>
                       n.ItemType == JsonItemType.Property && n.Value.Contains("strings." + stringResource.Name))
                   select new ReportItem
                   {
                       ProjectName = _projectName,
                       FullFileName = stringResource.FullFileName,
                       FileType = stringResource.FileType.ToString(),
                       LineId = stringResource.LineId.ToString(),
                       JsonPath = stringResource.JsonPath,
                       LineNumber = stringResource.SourceLineNumber.ToString(),
                       Message = $"String \"{stringResource.Name}\" is not used in the project",
                       ValidationType = ValidationTypeEnum.Logic.ToString(),
                       Severity = ImportanceEnum.Warning.ToString(),
                       Source = methodName
                   };
        }

        private static IEnumerable<ReportItem> CallNonExistingStrings(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.Value)
                    && !string.IsNullOrEmpty(n.Name));

            var fieldsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Value.Contains("{{strings."));

            return from field in fieldsList
                   let strList = GetTableField(field.Value)
                   from str in strList
                   where !string.IsNullOrEmpty(str) && str.StartsWith("strings.") &&
                         stringsList.All(n => n.Name != str.Replace("strings.", ""))
                   select new ReportItem
                   {
                       ProjectName = _projectName,
                       FullFileName = field.FullFileName,
                       FileType = field.FileType.ToString(),
                       LineId = field.LineId.ToString(),
                       JsonPath = field.JsonPath,
                       LineNumber = field.SourceLineNumber.ToString(),
                       Message = $"String \"{str}\" is not defined in the project",
                       ValidationType = ValidationTypeEnum.Logic.ToString(),
                       Severity = ImportanceEnum.Error.ToString(),
                       Source = methodName
                   };
        }

        private static IEnumerable<ReportItem> OverridingStrings(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var duplicateStringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.Value)
                    && !string.IsNullOrEmpty(n.Name))
                .GroupBy(n => n.Name)
                .Where(n => n.Count() > 1)
                .ToArray();

            var report = new List<ReportItem>();
            if (!duplicateStringsList.Any())
                return report;

            foreach (var dup in duplicateStringsList)
            {
                var strDup = dup.ToArray();
                for (var i = 1; i < strDup.Count(); i++)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = strDup[i - 1].FullFileName
                                       + _splitChar
                                       + strDup[i].FullFileName,
                        Message =
                            $"String \"{strDup[i - 1].Name}\" is overridden{Environment.NewLine} [\"{strDup[i - 1].Value}\" => \"{strDup[i].Value}\"]",
                        FileType = strDup[i - 1].FileType
                                   + _splitChar.ToString()
                                   + strDup[i].FileType,
                        LineId = strDup[i - 1].LineId
                                 + _splitChar.ToString()
                                 + strDup[i].LineId,
                        JsonPath = strDup[i - 1].JsonPath
                                   + _splitChar
                                   + strDup[i].JsonPath,
                        LineNumber = strDup[i - 1].SourceLineNumber.ToString()
                                     + _splitChar
                                     + strDup[i].SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> EmptyEventNames(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var emptyIdsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && string.IsNullOrEmpty(n.Value));

            return emptyIdsList.Select(id => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = id.FullFileName,
                FileType = id.FileType.ToString(),
                LineId = id.LineId.ToString(),
                JsonPath = id.JsonPath,
                LineNumber = id.SourceLineNumber.ToString(),
                Message = $"Event id \"{id.Value}\" is empty",
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Error.ToString(),
                Source = methodName
            });
        }

        private static IEnumerable<ReportItem> EmptyEvents(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var emptyEventsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !n.Shared
                    && !string.IsNullOrEmpty(n.Value));

            return from id in emptyEventsList
                   let objectMembers = _jsonPropertiesCollection.Where(n =>
                       n.ItemType == JsonItemType.Property && n.FullFileName == id.FullFileName &&
                       n.ParentPath.Contains(id.ParentPath + ".actions[") && n.Name == "type")
                   let actionFound = objectMembers.Any()
                   where !actionFound
                   select new ReportItem
                   {
                       ProjectName = _projectName,
                       FullFileName = id.FullFileName,
                       Message = $"Event \"{id.Value}\" has no actions",
                       FileType = id.FileType.ToString(),
                       LineId = id.LineId.ToString(),
                       JsonPath = id.JsonPath,
                       LineNumber = id.SourceLineNumber.ToString(),
                       ValidationType = ValidationTypeEnum.Logic.ToString(),
                       Severity = ImportanceEnum.Note.ToString(),
                       Source = methodName
                   };
        }

        private static IEnumerable<ReportItem> OverridingEvents(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var duplicateIdsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1).ToArray();

            var report = new List<ReportItem>();
            if (!duplicateIdsList.Any())
                return report;

            foreach (var dup in duplicateIdsList)
            {
                // duplicate "id" within project (not including shared imports)
                var projectDuplicates = dup.Where(n => !n.Shared).ToList();
                if (projectDuplicates.Count > 1)
                    for (var i = projectDuplicates.Count - 1; i > 0; i--)
                    {
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = projectDuplicates[i - 1].FullFileName
                                           + _splitChar
                                           + projectDuplicates[i].FullFileName,
                            Message = $"Event \"{projectDuplicates[i - 1].Value}\" is overridden",
                            FileType = projectDuplicates[i - 1].FileType
                                       + _splitChar.ToString()
                                       + projectDuplicates[i].FileType,
                            LineId = projectDuplicates[i - 1].LineId
                                     + _splitChar.ToString()
                                     + projectDuplicates[i].LineId,
                            JsonPath = projectDuplicates[i - 1].JsonPath
                                       + _splitChar
                                       + projectDuplicates[i].JsonPath,
                            LineNumber = projectDuplicates[i - 1].SourceLineNumber.ToString()
                                         + _splitChar
                                         + projectDuplicates[i].SourceLineNumber.ToString(),
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }

                // overriding shared "id" (project override shared)
                var sharedDuplicates = dup.Where(n => n.Shared).ToArray();
                if (!sharedDuplicates.Any() || !projectDuplicates.Any())
                    continue;

                var actionFound = _jsonPropertiesCollection
                    .Where(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FullFileName == sharedDuplicates.Last().FullFileName
                        && n.ParentPath.Contains(sharedDuplicates.Last().ParentPath + ".actions["))
                    .Any(n => n.Name == "type");

                if (actionFound)
                {
                    //non-empty shared method override
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = sharedDuplicates.Last().FullFileName
                                       + _splitChar
                                       + projectDuplicates.Last().FullFileName,
                        Message = $"Shared event \"{sharedDuplicates.Last().Value}\" is overridden",
                        FileType = sharedDuplicates.Last().FileType
                                   + _splitChar.ToString()
                                   + projectDuplicates.Last().FileType,
                        LineId = sharedDuplicates.Last().LineId
                                 + _splitChar.ToString()
                                 + projectDuplicates.Last().LineId,
                        JsonPath = sharedDuplicates.Last().JsonPath
                                   + _splitChar
                                   + projectDuplicates.Last().JsonPath,
                        LineNumber = sharedDuplicates.Last().SourceLineNumber.ToString()
                                     + _splitChar
                                     + projectDuplicates.Last().SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> RedundantEvents(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var idProjectList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !n.Shared
                    && !string.IsNullOrEmpty(n.Value));

            return from id in idProjectList
                   where !_jsonPropertiesCollection.Any(n =>
                       n.ItemType == JsonItemType.Property && n.FullFileName == id.FullFileName &&
                       n.ParentPath == id.ParentPath && n.Name == "trigger")
                   where !_jsonPropertiesCollection.Any(n =>
                       n.ItemType == JsonItemType.Property && n.FileType == KineticContentType.Events && n.Name != "id" &&
                       n.Value == id.Value)
                   select new ReportItem
                   {
                       ProjectName = _projectName,
                       FullFileName = id.FullFileName,
                       FileType = id.FileType.ToString(),
                       LineId = id.LineId.ToString(),
                       JsonPath = id.JsonPath,
                       LineNumber = id.SourceLineNumber.ToString(),
                       Message = $"Event id \"{id.Value}\" is not used in the project",
                       ValidationType = ValidationTypeEnum.Logic.ToString(),
                       Severity = ImportanceEnum.Warning.ToString(),
                       Source = methodName
                   };
        }

        private static IEnumerable<ReportItem> CallNonExistingEvents(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var idProjectList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !string.IsNullOrEmpty(n.Value));

            // call to non-existing "id" - for "value" && path+"type": "event-next", "iterativeEvent", 
            var fieldsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Name == "value"
                    && _jsonPropertiesCollection.Any(m =>
                        m.FullFileName == n.FullFileName
                        && m.ParentPath == n.ParentPath
                        && m.Name == "type"
                        && m.Value == "event-next")
                    || n.Name == "iterativeEvent")
                .Where(n =>
                    !n.Value.Contains('%')
                    && !n.Value.Contains('{')
                    && idProjectList.All(m => m.Value != n.Value));

            return fieldsList.Select(field => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = field.FullFileName,
                FileType = field.FileType.ToString(),
                LineId = field.LineId.ToString(),
                JsonPath = field.JsonPath,
                LineNumber = field.SourceLineNumber.ToString(),
                Message = $"Event id \"{field.Value}\" is not defined in the project",
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Error.ToString(),
                Source = methodName
            });
        }

        private static IEnumerable<ReportItem> EmptyDataViewNames(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var emptyDataViewList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.DataViews
                    && !n.Shared
                    && n.Parent == "dataviews"
                    && n.Name == "id"
                    && string.IsNullOrEmpty(n.Value));

            return emptyDataViewList.Select(viewResource => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = viewResource.FullFileName,
                FileType = viewResource.FileType.ToString(),
                LineId = viewResource.LineId.ToString(),
                JsonPath = viewResource.JsonPath,
                LineNumber = viewResource.SourceLineNumber.ToString(),
                Message = $"DataView id \"{viewResource.Value}\" is empty",
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Error.ToString(),
                Source = methodName
            });
        }

        private static IEnumerable<ReportItem> RedundantDataViews(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var dataViewList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.DataViews
                    && !n.Shared
                    && n.Parent == "dataviews"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value));

            return from viewResource in dataViewList
                   where !_jsonPropertiesCollection.Any(n =>
                       n.ItemType == JsonItemType.Property && n.FileType != KineticContentType.DataViews &&
                       n.Value.Contains(viewResource.Value))
                   select new ReportItem
                   {
                       ProjectName = _projectName,
                       FullFileName = viewResource.FullFileName,
                       FileType = viewResource.FileType.ToString(),
                       LineId = viewResource.LineId.ToString(),
                       JsonPath = viewResource.JsonPath,
                       LineNumber = viewResource.SourceLineNumber.ToString(),
                       Message = $"DataView \"{viewResource.Value}\" is not used in the project",
                       ValidationType = ValidationTypeEnum.Logic.ToString(),
                       Severity = ImportanceEnum.Warning.ToString(),
                       Source = methodName
                   };
        }

        private static IEnumerable<ReportItem> CallNonExistingDataViews(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var dataViewList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.DataViews
                    && n.Parent == "dataviews"
                    && n.Name == "id"
                    || n.FileType == KineticContentType.Events
                    && n.Parent == "param"
                    && n.Name == "result"
                    && !string.IsNullOrEmpty(n.Value));

            var report = new List<ReportItem>();
            foreach (var property in _jsonPropertiesCollection)
            {
                if (property.ItemType != JsonItemType.Property)
                    continue;

                var usedDataViewsList = new List<string>();

                if (property.FileType == KineticContentType.Events
                    && property.Parent == "trigger"
                    && property.Name == "target"
                    && !string.IsNullOrEmpty(property.Value)
                    && property.Value.Contains('.')
                    && _jsonPropertiesCollection
                        .Any(m =>
                            m.ItemType == JsonItemType.Property
                            && m.FullFileName == property.FullFileName
                            && m.ParentPath == property.ParentPath
                            && m.Name == "type"
                            && m.Value == "EpBinding"))
                {
                    usedDataViewsList = GetTableField(property.Value);
                }
                else if (property.FileType == KineticContentType.Events
                         && property.Parent == "trigger"
                         && property.Name == "target"
                         && !string.IsNullOrEmpty(property.Value)
                         && !property.Value.Contains('.')
                         && _jsonPropertiesCollection.Any(m =>
                             m.ItemType == JsonItemType.Property
                             && m.FullFileName == property.FullFileName
                             && m.ParentPath == property.ParentPath
                             && m.Name == "type"
                             && m.Value == "DataView"))
                {
                    usedDataViewsList.Add(property.Value + ".");
                }
                else if (property.Name == "epBinding" && !string.IsNullOrEmpty(property.Value))
                {
                    if (property.Value.Contains('.'))
                        usedDataViewsList = GetTableField(property.Value);
                    else
                        usedDataViewsList.Add(property.Value + ".");
                }
                else if (!string.IsNullOrEmpty(property.Value))
                {
                    usedDataViewsList = GetTableField(property.Value);
                }

                report.AddRange(from dataViewItem in usedDataViewsList
                                where !string.IsNullOrEmpty(dataViewItem)
                                select dataViewItem.Substring(0, dataViewItem.IndexOf('.'))
                    into viewName
                                where !_systemDataViews.Contains(viewName) && !viewName.StartsWith("%") &&
                                      dataViewList.All(n => n.Value != viewName)
                                select new ReportItem
                                {
                                    ProjectName = _projectName,
                                    FullFileName = property.FullFileName,
                                    FileType = property.FileType.ToString(),
                                    LineId = property.LineId.ToString(),
                                    JsonPath = property.JsonPath,
                                    LineNumber = property.SourceLineNumber.ToString(),
                                    Message = $"DataView \"{viewName}\" is not defined in the project",
                                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                                    Severity = ImportanceEnum.Error.ToString(),
                                    Source = methodName
                                });
            }

            return report;
        }

        private static IEnumerable<ReportItem> OverridingDataViews(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var duplicateViewsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.DataViews
                    && n.Parent == "dataviews"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1)
                .ToArray();

            var report = new List<ReportItem>();
            if (!duplicateViewsList.Any())
                return report;

            foreach (var dup in duplicateViewsList)
            {
                var viewDup = dup.ToArray();
                for (var i = 1; i < viewDup.Count(); i++)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = viewDup[i - 1].FullFileName
                                       + _splitChar
                                       + viewDup[i].FullFileName,
                        Message = $"DataView \"{viewDup[i - 1].Value}\" is overridden",
                        FileType = viewDup[i - 1].FileType
                                   + _splitChar.ToString()
                                   + viewDup[i].FileType,
                        LineId = viewDup[i - 1].LineId
                                 + _splitChar.ToString()
                                 + viewDup[i].LineId,
                        JsonPath = viewDup[i - 1].JsonPath
                                   + _splitChar
                                   + viewDup[i].JsonPath,
                        LineNumber = viewDup[i - 1].SourceLineNumber.ToString()
                                     + _splitChar
                                     + viewDup[i].SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> CallNonExistingDataTables(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var dataTableList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.DataViews
                    && n.Parent == "dataviews"
                    && n.Name == "table"
                    && !string.IsNullOrEmpty(n.Value))
                .Select(n => n.Value)
                .Distinct()
                .ToArray();

            var report = new List<ReportItem>();
            foreach (var property in _jsonPropertiesCollection)
            {
                if (property.ItemType != JsonItemType.Property)
                    continue;

                var usedDataTable = "";

                if (property.FileType == KineticContentType.Events
                    && property.Parent == "trigger"
                    && property.Name == "target"
                    && !string.IsNullOrEmpty(property.Value)
                    && !property.Value.Contains('.')
                    && _jsonPropertiesCollection.Any(m =>
                        m.ItemType == JsonItemType.Property
                        && m.FullFileName == property.FullFileName
                        && m.ParentPath == property.ParentPath
                        && m.Name == "type"
                        && m.Value == "DataTable"))
                    usedDataTable = property.Value;

                if (string.IsNullOrEmpty(usedDataTable))
                    continue;

                if (!usedDataTable.StartsWith("%")
                    && !dataTableList.Contains(usedDataTable))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = property.FullFileName,
                        FileType = property.FileType.ToString(),
                        LineId = property.LineId.ToString(),
                        JsonPath = property.JsonPath,
                        LineNumber = property.SourceLineNumber.ToString(),
                        Message = $"DataTable \"{usedDataTable}\" is not defined in the project",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> EmptyRuleNames(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var emptyRulesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Rules
                    && n.Parent == "rules"
                    && n.Name == "id"
                    && string.IsNullOrEmpty(n.Value));

            return emptyRulesList.Select(dup => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = dup.FullFileName,
                Message = $"Rule id \"{dup.Value}\" is empty",
                FileType = dup.FileType.ToString(),
                LineId = dup.LineId.ToString(),
                JsonPath = dup.JsonPath,
                LineNumber = dup.SourceLineNumber.ToString(),
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Error.ToString(),
                Source = methodName
            });
        }

        private static IEnumerable<ReportItem> OverridingRules(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var duplicateRulesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Rules
                    && n.Parent == "rules"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1)
                .ToArray();

            var report = new List<ReportItem>();
            if (!duplicateRulesList.Any())
                return report;

            foreach (var dup in duplicateRulesList)
            {
                var ruleDup = dup.ToArray();
                for (var i = 1; i < ruleDup.Count(); i++)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = ruleDup[i - 1].FullFileName
                                       + _splitChar
                                       + ruleDup[i].FullFileName,
                        Message = $"Rule \"{ruleDup[i - 1].Value}\" is overridden",
                        FileType = ruleDup[i - 1].FileType
                                   + _splitChar.ToString()
                                   + ruleDup[i].FileType,
                        LineId = ruleDup[i - 1].LineId
                                 + _splitChar.ToString()
                                 + ruleDup[i].LineId,
                        JsonPath = ruleDup[i - 1].JsonPath
                                   + _splitChar
                                   + ruleDup[i].JsonPath,
                        LineNumber = ruleDup[i - 1].SourceLineNumber.ToString()
                                     + _splitChar
                                     + ruleDup[i].SourceLineNumber,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> EmptyToolNames(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var emptyToolsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Tools
                    && n.Parent == "tools"
                    && n.Name == "id"
                    && string.IsNullOrEmpty(n.Value));

            return emptyToolsList.Select(dup => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = dup.FullFileName,
                Message = $"Tool id \"{dup.Value}\" is empty",
                FileType = dup.FileType.ToString(),
                LineId = dup.LineId.ToString(),
                JsonPath = dup.JsonPath,
                LineNumber = dup.SourceLineNumber.ToString(),
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Error.ToString(),
                Source = methodName
            });
        }

        private static IEnumerable<ReportItem> OverridingTools(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var duplicateToolsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Tools
                    && n.Parent == "tools"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1)
                .ToArray();

            var report = new List<ReportItem>();
            if (!duplicateToolsList.Any())
                return report;

            foreach (var dup in duplicateToolsList)
            {
                var toolDup = dup.ToArray();
                for (var i = 1; i < toolDup.Count(); i++)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = toolDup[i - 1].FullFileName
                                       + _splitChar
                                       + toolDup[i].FullFileName,
                        Message = $"Tool \"{toolDup[i - 1].Value}\" is overridden",
                        FileType = toolDup[i - 1].FileType
                                   + _splitChar.ToString()
                                   + toolDup[i].FileType,
                        LineId = toolDup[i - 1].LineId
                                 + _splitChar.ToString()
                                 + toolDup[i].LineId,
                        JsonPath = toolDup[i - 1].JsonPath
                                   + _splitChar
                                   + toolDup[i].JsonPath,
                        LineNumber = toolDup[i - 1].SourceLineNumber.ToString()
                                     + _splitChar
                                     + toolDup[i].SourceLineNumber,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> MissingForms(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var formsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "view"
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Parent == "param"
                    && _jsonPropertiesCollection.Any(m =>
                        n.ItemType == JsonItemType.Property
                        && m.Name == "type"
                        && m.Value == "app-open"
                        && m.FullFileName == n.FullFileName
                        && m.FileType == KineticContentType.Events
                        && m.ParentPath + ".param" == n.ParentPath));

            var report = new List<ReportItem>();
            foreach (var form in formsList)
            {
                List<string> formFileList;

                switch (_folderType)
                {
                    case FolderType.Unknown:
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = form.FullFileName,
                            Message = "Folder type not recognized (Deployment/Repository/...)",
                            FileType = form.FileType.ToString(),
                            LineId = form.LineId.ToString(),
                            JsonPath = form.JsonPath,
                            LineNumber = form.SourceLineNumber.ToString(),
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Warning.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);

                        formFileList = new List<string>
                        {
                            _projectPath + "\\..\\" + form.Value + "\\events.jsonc"
                        };
                        break;

                    case FolderType.Deployment:
                        formFileList = new List<string>
                        {
                            _projectPath + "\\..\\" + form.Value + "\\events.jsonc"
                        };
                        break;

                    case FolderType.IceRepository:
                        formFileList = new List<string>
                        {
                            _projectPath + "\\..\\..\\..\\UIApps\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\UIProc\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\UIReports\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\UITrackers\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\ICE\\UIApps\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\ICE\\UIProc\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\ICE\\UIReports\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\ICE\\UITrackers\\" + form.Value + "\\events.jsonc"
                        };
                        break;

                    default:
                        formFileList = new List<string>
                        {
                            _projectPath + "\\..\\..\\UIApps\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\UIProc\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\UIReports\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\UITrackers\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\ICE\\UIApps\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\ICE\\UIProc\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\ICE\\UIReports\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\ICE\\UITrackers\\" + form.Value + "\\events.jsonc"
                        };
                        break;
                }

                var fileFound = formFileList.Any(File.Exists);

                if (!fileFound)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = form.FullFileName,
                        Message = $"Call to non-existing form \"{form.Value}\" (Could it be a WinForm?)",
                        FileType = form.FileType.ToString(),
                        LineId = form.LineId.ToString(),
                        JsonPath = form.JsonPath,
                        LineNumber = form.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> MissingSearches(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var searchesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "like"
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Parent == "searchOptions"
                    && _jsonPropertiesCollection.Any(m =>
                        m.ItemType == JsonItemType.Property
                        && m.Name == "searchForm"
                        && m.FullFileName == n.FullFileName
                        && m.FileType == KineticContentType.Events
                        && m.ParentPath == n.ParentPath));

            var report = new List<ReportItem>();
            foreach (var form in searchesList)
            {
                var formSubFolder = _jsonPropertiesCollection
                    .Where(m =>
                        m.ItemType == JsonItemType.Property
                        && m.Name == "searchForm"
                        && !string.IsNullOrEmpty(m.Value)
                        && m.FullFileName == form.FullFileName
                        && m.FileType == KineticContentType.Events
                        && m.ParentPath == form.ParentPath)
                    .ToArray();

                if (!formSubFolder.Any())
                    continue;

                var searchName = form.Value
                                 + "\\"
                                 + formSubFolder.FirstOrDefault()?.Value;
                var searchFile = "";
                switch (_folderType)
                {
                    case FolderType.Unknown:
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = form.FullFileName,
                            Message = "Folder type not recognized (Deployment/Repository/...)",
                            FileType = form.FileType.ToString(),
                            LineId = form.LineId.ToString(),
                            JsonPath = form.JsonPath,
                            LineNumber = form.SourceLineNumber.ToString(),
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Warning.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);

                        searchFile = _projectPath
                                     + "\\..\\Shared\\search\\"
                                     + searchName
                                     + "\\search.jsonc";
                        break;

                    case FolderType.Deployment:
                        searchFile = _projectPath
                                     + "\\..\\Shared\\search\\"
                                     + searchName
                                     + "\\search.jsonc";
                        break;

                    case FolderType.IceRepository:
                        searchFile = _projectPath
                                     + "\\..\\..\\..\\Shared\\search\\"
                                     + searchName
                                     + "\\search.jsonc";
                        break;

                    default:
                        searchFile = _projectPath
                                     + "\\..\\..\\Shared\\search\\"
                                     + searchName
                                     + "\\search.jsonc";
                        break;
                }

                if (!File.Exists(searchFile))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = form.FullFileName,
                        Message = $"Call to non-existing search \"{searchName}\"",
                        FileType = form.FileType.ToString(),
                        LineId = form.LineId.ToString(),
                        JsonPath = form.JsonPath,
                        LineNumber = form.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> JsCode(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var jsPatternsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && HasJsCode(n.Value));

            return jsPatternsList.Select(jsCode => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = jsCode.FullFileName,
                Message = $"Property value contains JS code \"{jsCode.Value}\"",
                FileType = jsCode.FileType.ToString(),
                LineId = jsCode.LineId.ToString(),
                JsonPath = jsCode.JsonPath,
                LineNumber = jsCode.SourceLineNumber.ToString(),
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Note.ToString(),
                Source = methodName
            });
        }

        private static IEnumerable<ReportItem> JsDataViewCount(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var jsPatternsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && HasJsCode(n.Value)
                    && HasJsDvCount(n.Value));

            return jsPatternsList.Select(pattern => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = pattern.FullFileName,
                Message = $"JS code \"{pattern.Value}\" must be replaced to \"%DataView.count%\"",
                FileType = pattern.FileType.ToString(),
                LineId = pattern.LineId.ToString(),
                JsonPath = pattern.JsonPath,
                LineNumber = pattern.SourceLineNumber.ToString(),
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Warning.ToString(),
                Source = methodName
            });
        }

        private static IEnumerable<ReportItem> MissingLayoutIds(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var layoutIdList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Layout
                    && n.JsonDepth == 2
                    && n.Name == "id");

            return from idItem in layoutIdList
                   let modelId = _jsonPropertiesCollection.Where(n =>
                       n.FullFileName == idItem.FullFileName && n.ItemType == JsonItemType.Property &&
                       n.FileType == KineticContentType.Layout && n.ParentPath == idItem.ParentPath + ".model" &&
                       n.Name == "id").ToArray()
                   where !modelId.Any()
                   select new ReportItem
                   {
                       ProjectName = _projectName,
                       FullFileName = idItem.FullFileName,
                       Message = $"Layout control id=\"{idItem.Value}\" has no model id",
                       FileType = idItem.FileType.ToString(),
                       LineId = idItem.LineId.ToString(),
                       JsonPath = idItem.JsonPath,
                       LineNumber = idItem.SourceLineNumber.ToString(),
                       ValidationType = ValidationTypeEnum.Logic.ToString(),
                       Severity = ImportanceEnum.Error.ToString(),
                       Source = methodName
                   };
        }

        private static IEnumerable<ReportItem> IncorrectLayoutIds(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var layoutIdList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Layout
                    && n.JsonDepth == 2
                    && n.Name == "id");

            return from idItem in layoutIdList
                   let modelId = _jsonPropertiesCollection.Where(n =>
                       n.FullFileName == idItem.FullFileName && n.ItemType == JsonItemType.Property &&
                       n.FileType == KineticContentType.Layout && n.ParentPath == idItem.ParentPath + ".model" &&
                       n.Name == "id").ToArray()
                   where modelId.Any()
                   where idItem.Value != modelId.First().Value
                   select new ReportItem
                   {
                       ProjectName = _projectName,
                       FullFileName = idItem.FullFileName + _splitChar + modelId.First().FullFileName,
                       Message =
                           $"Layout control id=\"{idItem.Value}\" doesn't match model id=\"{modelId.First().Value}\"",
                       FileType = idItem.FileType.ToString() + _splitChar + modelId.First().FileType,
                       LineId = idItem.LineId.ToString() + _splitChar + modelId.First().LineId,
                       JsonPath = idItem.JsonPath + _splitChar + modelId.First().JsonPath,
                       LineNumber = idItem.SourceLineNumber.ToString() + _splitChar + modelId.First().SourceLineNumber.ToString(),
                       ValidationType = ValidationTypeEnum.Logic.ToString(),
                       Severity = ImportanceEnum.Error.ToString(),
                       Source = methodName
                   };
        }

        private static IEnumerable<ReportItem> IncorrectDvConditionViewName(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var dvConditionsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "type"
                    && n.Value == "dataview-condition");

            var report = new List<ReportItem>();
            foreach (var item in dvConditionsList)
            {
                var dvName = _jsonPropertiesCollection
                    .Where(n =>
                        n.FullFileName == item.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.Events
                        && n.ParentPath == item.ParentPath + ".param"
                        && n.Name == "dataview").ToArray();

                var resultDvName = _jsonPropertiesCollection
                    .Where(n =>
                        n.FullFileName == item.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.Events
                        && n.ParentPath == item.ParentPath + ".param"
                        && n.Name == "result").ToArray();

                if (dvName.Length != 1 || resultDvName.Length != 1)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = "Incorrect dataview-condition definition",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        LineNumber = item.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }

                if (dvName[0].Value == resultDvName[0].Value)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message =
                            "Incorrect dataview-condition definition: \"dataview\" should be different from \"result\"",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = dvName[0].JsonPath,
                        LineNumber = dvName[0].SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> IncorrectTabIds(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var tabIdsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Layout
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Name == "tabId").Select(n => n.Value).ToArray();

            var tabStripsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Layout
                    && n.Name == "sourceTypeId"
                    && n.Value == "metafx-tabstrip");

            return from tab in tabStripsList
                   from item in _jsonPropertiesCollection.Where(n =>
                       !n.Shared && n.FullFileName == tab.FullFileName && n.ItemType == JsonItemType.Property &&
                       n.FileType == KineticContentType.Layout && n.JsonPath.Contains(tab.ParentPath + ".model.data") &&
                       n.Name == "page" && !string.IsNullOrEmpty(n.Value))
                   where !tabIdsList.Contains(item.Value)
                   select new ReportItem
                   {
                       ProjectName = _projectName,
                       FullFileName = item.FullFileName,
                       Message = $"Inexistent tab link: {item.Value}",
                       FileType = item.FileType.ToString(),
                       LineId = item.LineId.ToString(),
                       JsonPath = item.JsonPath,
                       LineNumber = item.SourceLineNumber.ToString(),
                       ValidationType = ValidationTypeEnum.Logic.ToString(),
                       Severity = ImportanceEnum.Error.ToString(),
                       Source = methodName
                   };
        }

        private static IEnumerable<ReportItem> IncorrectRestCalls(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);

            var restCallsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Value == "rest-erp"
                    && n.Name == "type");

            var report = new List<ReportItem>();
            foreach (var item in restCallsList)
            {
                var serviceName = _jsonPropertiesCollection.FirstOrDefault(n => !n.Shared
                    && n.FullFileName == item.FullFileName
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.JsonPath == item.ParentPath + ".param.svc"
                    && !string.IsNullOrEmpty(n.Value));

                var svcMethodName = _jsonPropertiesCollection.FirstOrDefault(n => !n.Shared
                    && n.FullFileName == item.FullFileName
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.JsonPath == item.ParentPath + ".param.svcPath"
                    && !string.IsNullOrEmpty(n.Value));

                var methodParamsList = _jsonPropertiesCollection
                    .Where(n =>
                        !n.Shared
                        && n.FullFileName == item.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.Events
                        && (!GetParentName(n.ParentPath).StartsWith("params[")
                            && n.JsonPath.StartsWith(item.ParentPath + ".param.methodParameters")
                            && n.Name == "field"
                            || n.JsonPath.StartsWith(item.ParentPath + ".param.erpRestPostArgs")
                            && n.Name == "paramPath"))
                    .ToArray();

                if (serviceName == null || string.IsNullOrEmpty(serviceName.Value))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = "REST service name not defined",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        LineNumber = item.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                    continue;
                }

                if (IsPatch(serviceName.Value) || IsTableField(serviceName.Value) ||
                    HasJsCode(serviceName.Value))
                    continue;

                if (svcMethodName == null)
                    continue;

                var serverParams = GetRestCallParams(serviceName.Value, svcMethodName.Value, _serverAssembliesPath);

                if (string.IsNullOrEmpty(serverParams.SvcName))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = $"Incorrect REST service name: {serviceName.Value}",
                        FileType = serviceName.FileType.ToString(),
                        LineId = serviceName.LineId.ToString(),
                        JsonPath = serviceName.JsonPath,
                        LineNumber = serviceName.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                    continue;
                }

                if (IsPatch(svcMethodName.Value) || IsTableField(svcMethodName.Value) ||
                    HasJsCode(svcMethodName.Value))
                    continue;

                if (string.IsNullOrEmpty(svcMethodName.Value))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = "REST method name not defined",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        LineNumber = item.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                    continue;
                }

                if (string.IsNullOrEmpty(serverParams.MethodName))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = $"Incorrect REST service '{serviceName.Value}' method name: {svcMethodName.Value}",
                        FileType = svcMethodName.FileType.ToString(),
                        LineId = svcMethodName.LineId.ToString(),
                        JsonPath = svcMethodName.JsonPath,
                        LineNumber = svcMethodName.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);

                    continue;
                }

                // if failed to get params (no Epicor.ServceModel.dll found)
                if (serverParams.Parameters == null)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message =
                            $"Failed to retrieve REST service '{serviceName.Value}' method '{svcMethodName.Value}' parameters (Epicor.ServiceMode.dll not found?)",
                        FileType = svcMethodName.FileType.ToString(),
                        LineId = svcMethodName.LineId.ToString(),
                        JsonPath = svcMethodName.ParentPath,
                        LineNumber = svcMethodName.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);

                    continue;
                }

                var missingParams = new StringBuilder();
                if (methodParamsList.Any())
                {
                    foreach (var par in methodParamsList)
                    {
                        if (IsPatch(par.Value) || IsTableField(par.Value) || HasJsCode(par.Value))
                            continue;

                        if (!serverParams.Parameters.Contains(par.Value))
                            missingParams.Append(par.Value + ", ");
                    }

                    if (missingParams.Length > 0)
                    {
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = item.FullFileName,
                            Message =
                                $"Incorrect REST service '{serviceName.Value}' method '{svcMethodName.Value}' parameter names: {missingParams}",
                            FileType = methodParamsList.FirstOrDefault()?.FileType.ToString(),
                            LineId = methodParamsList.FirstOrDefault()?.LineId.ToString(),
                            JsonPath = methodParamsList.FirstOrDefault()?.ParentPath,
                            LineNumber = methodParamsList.FirstOrDefault()?.SourceLineNumber.ToString(),
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
                }

                missingParams = new StringBuilder();
                if (!serverParams.Parameters.Any())
                    continue;

                var methodParamsTmp = methodParamsList.Select(t => t.Value).ToArray();
                foreach (var par in serverParams.Parameters.Where(t => t != "ds"))
                    if (!methodParamsTmp.Contains(par))
                        missingParams.Append(par + ", ");

                if (missingParams.Length > 0)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message =
                            $"Missing REST service '{serviceName.Value}' method '{svcMethodName.Value}' parameter names: {missingParams}",
                        FileType = svcMethodName.FileType.ToString(),
                        LineId = svcMethodName.LineId.ToString(),
                        JsonPath = svcMethodName.ParentPath + ".methodParameters",
                        LineNumber = svcMethodName.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> DuplicateGuiDs(string methodName)
        {
            while (_patchAllFields && !_allFieldsPatched)
                Thread.Sleep(1);


            var item2 = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Layout
                    && n.Name == "guid").GroupBy(n => n.Value).Where(n => n.Count() > 1);

            var guidList = item2 as IGrouping<string, JsonProperty>[] ?? item2.ToArray();

            if (!guidList.Any())
                return new List<ReportItem>();

            return guidList.Select(dup => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = dup.FirstOrDefault()?.FullFileName,
                Message = $"Project has duplicate GUID \"{dup.FirstOrDefault()?.Value}\"",
                FileType = dup.FirstOrDefault()?.FileType.ToString(),
                LineId = dup.FirstOrDefault()?.LineId.ToString(),
                JsonPath = dup.FirstOrDefault()?.JsonPath,
                LineNumber = dup.FirstOrDefault()?.SourceLineNumber.ToString(),
                ValidationType = ValidationTypeEnum.Parse.ToString(),
                Severity = ImportanceEnum.Warning.ToString(),
                Source = methodName
            });
        }

        #endregion

        // misprints in property/dataview/string/id names (try finding lower-case property in schema or project scope)
        // searches must use only one dataview
    }
}
