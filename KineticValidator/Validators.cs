using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KineticValidator
{
    public partial class MainForm
    {
        private void RunValidation_dummy() { }

        private void DeserializeFile_dummy() { }

        private void ParseJsonObject_dummy() { }

        private void SchemaValidation()
        {
            // validate every file with schema
            var totalFiles = _processedFilesList.Count;
            var i1 = 1;

            foreach (var file in _processedFilesList)
            {
                SetStatus($"Validation scheme: {file.Key} [{i1}/{totalFiles}]");
                ValidateFileSchema(file.Key, file.Value);
                i1++;
            }
        }

        private void RedundantFiles()
        {
            // collect full list of files inside the project folder (not including shared)
            var fullFilesList = new List<string>();
            fullFilesList.AddRange(Directory.GetFiles(_projectPath, FileMask, SearchOption.AllDirectories));

            foreach (var file in fullFilesList)
            {
                if (file.IndexOf(_projectPath + "\\views\\", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                if (!_processedFilesList.ContainsKey(file)
                    && !IsShared(file))
                {
                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = file,
                        Message = "File is not used in the project",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Note.ToString(),
                        Source = "RedundantFiles"
                    };
                    _reportsCollection.Add(report);
                }
            }
        }

        private void DuplicateIds()
        {
            foreach (var file in _processedFilesList)
            {
                var propertyList = GetParsedFile(file.Key);

                var item2 = propertyList.Where(n =>
                    n.Type == JsonPathParser.PropertyType.Property)
                        .GroupBy(n => n.Path)
                        .Where(n => n.Count() > 1);

                var duplicateIdList = item2 as IGrouping<string, JsonPathParser.ParsedProperty>[] ?? item2.ToArray();
                if (duplicateIdList.Any())
                {
                    foreach (var dup in duplicateIdList)
                    {
                        string values = "";
                        foreach (var item in dup)
                        {
                            if (string.IsNullOrEmpty(values))
                            {
                                values += "\"" + item.Value + "\"";
                            }
                            else
                            {
                                values += ", \"" + item.Value + "\"";
                            }
                        }
                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = file.Key,
                            JsonPath = dup.First().Path,
                            Message = $"JSON file has duplicate property names \"{dup.First().Name}\" with values: {values}",
                            ValidationType = ValidationTypeEnum.Parse.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "DuplicateIds"
                        };
                        _reportsCollection.Add(report);
                    }
                }
            }
        }

        private void ValidateFileChars()
        {
            foreach (var item in _jsonPropertiesCollection)
            {
                if (item == null || item.ItemType != JsonItemType.Property || !string.IsNullOrEmpty(item.Value))
                    continue;

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

                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        Message = $"Json property \"{item.Name}\" has non-ASCII chars at position(s) {charPos}",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "ValidateFileChars"
                    };
                    _reportsCollection.Add(report);
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

                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        Message = $"JSON property value \"{item.Value}\" has non-ASCII chars at position(s) {charPos}",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = "ValidateFileChars"
                    };
                    _reportsCollection.Add(report);
                }
            }
        }

        private void EmptyStringNames()
        {
            var emptyStringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Strings
                    && n.Parent == "strings"
                    && string.IsNullOrEmpty(n.Name));

            foreach (var stringResource in emptyStringsList)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = stringResource.FullFileName,
                    FileType = stringResource.FileType.ToString(),
                    LineId = stringResource.LineId.ToString(),
                    JsonPath = stringResource.JsonPath,
                    Message = $"String id \"{stringResource.Name}\" is empty",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "EmptyStringNames"
                };
                _reportsCollection.Add(report);
            }
        }

        private void EmptyStringValues()
        {
            var emptyStringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Strings
                    && n.Parent == "strings"
                    && string.IsNullOrEmpty(n.Value));

            foreach (var stringResource in emptyStringsList)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = stringResource.FullFileName,
                    FileType = stringResource.FileType.ToString(),
                    LineId = stringResource.LineId.ToString(),
                    JsonPath = stringResource.JsonPath,
                    Message = $"String id \"{stringResource.Name}\" value is empty",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Warning.ToString(),
                    Source = "EmptyStringValues"
                };
                _reportsCollection.Add(report);
            }
        }

        private void RedundantStrings()
        {
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.Value)
                    && !string.IsNullOrEmpty(n.Name));

            foreach (var stringResource in stringsList)
                if (!string.IsNullOrEmpty(stringResource.Name)
                    && !_jsonPropertiesCollection.Any(n =>
                            n.ItemType == JsonItemType.Property
                            && n.Value.Contains("strings." + stringResource.Name)))
                {
                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = stringResource.FullFileName,
                        FileType = stringResource.FileType.ToString(),
                        LineId = stringResource.LineId.ToString(),
                        JsonPath = stringResource.JsonPath,
                        Message = $"String \"{stringResource.Name}\" is not used in the project",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = "RedundantStrings"
                    };
                    _reportsCollection.Add(report);
                }
        }

        private void CallNonExistingStrings()
        {
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.Value)
                    && !string.IsNullOrEmpty(n.Name));

            var fieldsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Value.Contains("{{strings."));

            foreach (var field in fieldsList)
            {
                var strList = GetValueCall(field.Value);
                foreach (var str in strList)
                    if (!string.IsNullOrEmpty(str)
                        && str.StartsWith("strings.")
                        && !stringsList
                        .Any(n => n.Name == str.Replace("strings.", "")))
                    {
                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = field.FullFileName,
                            FileType = field.FileType.ToString(),
                            LineId = field.LineId.ToString(),
                            JsonPath = field.JsonPath,
                            Message = $"String \"{str}\" is not defined in the project",
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "CallNonExistingStrings"
                        };
                        _reportsCollection.Add(report);
                    }
            }
        }

        private void OverridingStrings()
        {
            var duplicateStringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.Value)
                    && !string.IsNullOrEmpty(n.Name))
                .GroupBy(n => n.Name)
                .Where(n => n.Count() > 1);

            if (duplicateStringsList.Any())
                foreach (var dup in duplicateStringsList)
                {
                    var strDup = dup.ToArray();
                    for (var i = 1; i < strDup.Count(); i++)
                    {
                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = strDup[i - 1].FullFileName
                                           + SplitChar
                                           + strDup[i].FullFileName,
                            Message = $"String \"{strDup[i - 1].Name}\" is overridden{Environment.NewLine} [\"{strDup[i - 1].Value}\" => \"{strDup[i].Value}\"]",
                            FileType = strDup[i - 1].FileType
                                       + SplitChar.ToString()
                                       + strDup[i].FileType,
                            LineId = strDup[i - 1].LineId
                                     + SplitChar.ToString()
                                     + strDup[i].LineId,
                            JsonPath = strDup[i - 1].JsonPath
                                       + SplitChar
                                       + strDup[i].JsonPath,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "OverridingStrings"
                        };
                        _reportsCollection.Add(report);
                    }
                }
        }

        private void HardCodedStrings()
        {
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "message"
                    && !n.Value.Contains("{{strings.")
                    && !IsSingleValueCall(n.Value));

            foreach (var field in stringsList)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = field.FullFileName,
                    FileType = field.FileType.ToString(),
                    LineId = field.LineId.ToString(),
                    JsonPath = field.JsonPath,
                    Message = $"String \"{field.Value}\" should be moved to strings.jsonc resource file",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = "HardCodedStrings"
                };
                _reportsCollection.Add(report);
            }
        }

        private void PossibleStringsValues()
        {
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.Value)
                    && !string.IsNullOrEmpty(n.Name));

            var missedStringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType != JsoncContentType.Strings
                    && stringsList.Any(m =>
                        !string.IsNullOrEmpty(m.Value)
                        && m.Value == n.Value));

            foreach (var item in missedStringsList)
            {
                var slist = stringsList
                    .Where(m => m.Value == item.Value)
                    .Select(n => n.Name);
                var patches = "";

                foreach (var str in slist)
                {
                    if (!string.IsNullOrEmpty(patches))
                        patches += ", ";

                    patches += str;
                }

                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = item.FullFileName,
                    FileType = item.FileType.ToString(),
                    LineId = item.LineId.ToString(),
                    JsonPath = item.JsonPath,
                    Message = $"String \"{item.Value}\" can be replaced with string variable(s): {patches}",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = "PossibleStringsValues"
                };
                _reportsCollection.Add(report);
            }
        }

        private void EmptyEventNames()
        {
            var emptyIdsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && string.IsNullOrEmpty(n.Value));

            foreach (var id in emptyIdsList)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = id.FullFileName,
                    FileType = id.FileType.ToString(),
                    LineId = id.LineId.ToString(),
                    JsonPath = id.JsonPath,
                    Message = $"Event id \"{id.Value}\" is empty",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "EmptyEventNames"
                };
                _reportsCollection.Add(report);
            }
        }

        private void EmptyEvents()
        {
            var emptyEventsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !n.Shared
                    && !string.IsNullOrEmpty(n.Value));

            foreach (var id in emptyEventsList)
            {
                var objectMembers = _jsonPropertiesCollection
                    .Where(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FullFileName == id.FullFileName
                        && n.ParentPath.Contains(id.ParentPath + ".actions[")
                        && n.Name == "type");
                var actionFound = objectMembers.Any();

                if (!actionFound)
                {
                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = id.FullFileName,
                        Message = $"Event \"{id.Value}\" has no actions",
                        FileType = id.FileType.ToString(),
                        LineId = id.LineId.ToString(),
                        JsonPath = id.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Note.ToString(),
                        Source = "EmptyEvents"
                    };
                    _reportsCollection.Add(report);
                }
            }
        }

        private void RedundantEvents()
        {
            var idProjectList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !n.Shared
                    && !string.IsNullOrEmpty(n.Value));

            foreach (var id in idProjectList)
            {
                // check it's a trigger method called on change of smth.
                if (_jsonPropertiesCollection
                    .Any(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FullFileName == id.FullFileName
                        && n.ParentPath == id.ParentPath
                        && n.Name == "trigger"))
                    continue;

                if (!_jsonPropertiesCollection
                    .Any(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Events
                        && n.Name != "id"
                        && n.Value == id.Value))
                {
                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = id.FullFileName,
                        FileType = id.FileType.ToString(),
                        LineId = id.LineId.ToString(),
                        JsonPath = id.JsonPath,
                        Message = $"Event id \"{id.Value}\" is not used in the project",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = "RedundantEvents"
                    };
                    _reportsCollection.Add(report);
                }
            }
        }

        private void CallNonExistingEvents()
        {
            var idProjectList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
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

            foreach (var field in fieldsList)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = field.FullFileName,
                    FileType = field.FileType.ToString(),
                    LineId = field.LineId.ToString(),
                    JsonPath = field.JsonPath,
                    Message = $"Event id \"{field.Value}\" is not defined in the project",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "CallNonExistingEvents"
                };
                _reportsCollection.Add(report);
            }
        }

        private void OverridingEvents()
        {
            var duplicateIdsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1);

            if (duplicateIdsList.Any())
            {
                foreach (var dup in duplicateIdsList)
                {
                    // duplicate "id" within project (not including shared imports)
                    var projectDuplicates = dup.Where(n => !n.Shared).ToList();
                    if (projectDuplicates.Count > 1)
                        for (var i = projectDuplicates.Count - 1; i > 0; i--)
                        {
                            var report = new ReportItem
                            {
                                ProjectName = _projectName,
                                FullFileName = projectDuplicates[i - 1].FullFileName
                                               + SplitChar
                                               + projectDuplicates[i].FullFileName,
                                Message = $"Event \"{projectDuplicates[i - 1].Value}\" is overridden",
                                FileType = projectDuplicates[i - 1].FileType
                                           + SplitChar.ToString()
                                           + projectDuplicates[i].FileType,
                                LineId = projectDuplicates[i - 1].LineId
                                         + SplitChar.ToString()
                                         + projectDuplicates[i].LineId,
                                JsonPath = projectDuplicates[i - 1].JsonPath
                                           + SplitChar
                                           + projectDuplicates[i].JsonPath,
                                ValidationType = ValidationTypeEnum.Logic.ToString(),
                                Severity = ImportanceEnum.Error.ToString(),
                                Source = "OverridingEvents"
                            };
                            _reportsCollection.Add(report);
                        }

                    // overriding shared "id" (project override shared)
                    var sharedDuplicates = dup.Where(n => n.Shared);
                    if (sharedDuplicates.Any() && projectDuplicates.Any())
                    {
                        var objectMembers = _jsonPropertiesCollection
                            .Where(n =>
                                n.ItemType == JsonItemType.Property
                                && n.FullFileName == sharedDuplicates.Last().FullFileName
                                && n.ParentPath.Contains(sharedDuplicates.Last().ParentPath + ".actions["));
                        var actionFound = objectMembers.Any(n => n.Name == "type");

                        if (actionFound)
                        {
                            //non-empty shared method override
                            var report = new ReportItem
                            {
                                ProjectName = _projectName,
                                FullFileName = sharedDuplicates.Last().FullFileName
                                               + SplitChar
                                               + projectDuplicates.Last().FullFileName,
                                Message = $"Shared event \"{sharedDuplicates.Last().Value}\" is overridden",
                                FileType = sharedDuplicates.Last().FileType
                                           + SplitChar.ToString()
                                           + projectDuplicates.Last().FileType,
                                LineId = sharedDuplicates.Last().LineId
                                         + SplitChar.ToString()
                                         + projectDuplicates.Last().LineId,
                                JsonPath = sharedDuplicates.Last().JsonPath
                                           + SplitChar
                                           + projectDuplicates.Last().JsonPath,
                                ValidationType = ValidationTypeEnum.Logic.ToString(),
                                Severity = ImportanceEnum.Warning.ToString(),
                                Source = "OverridingEvents"
                            };
                            _reportsCollection.Add(report);
                        }
                    }
                }
            }
        }

        private void EmptyPatchNames()
        {
            var emptPatchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Patch
                    && !n.Shared
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && string.IsNullOrEmpty(n.Value));
            foreach (var patchResource in emptPatchList)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = patchResource.FullFileName,
                    FileType = patchResource.FileType.ToString(),
                    LineId = patchResource.LineId.ToString(),
                    JsonPath = patchResource.JsonPath,
                    Message = $"Patch id \"{patchResource.Value}\" is empty",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "EmptyPatchNames"
                };
                _reportsCollection.Add(report);
            }
        }

        private Dictionary<string, string> CollectPatchValues()
        {
            var patchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Patch
                    && n.Parent == "patch"
                    && n.Name == "id");

            var patchValues = new Dictionary<string, string>();
            foreach (var item in patchList)
            {
                var newValue = _jsonPropertiesCollection
                    .LastOrDefault(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Patch
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
                && n.Value.Trim().EndsWith("%"));

                allReplaced = true;

                if (toPatch.Any())
                {
                    var replaceValues = new Dictionary<string, string>();
                    foreach (var item in toPatch)
                        foreach (var patch in patchValues)
                            if (patch.Key == item.Value)
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

        private void PossiblePatchValues()
        {
            if (_patchValues == null || _patchValues.Count == 0)
                _patchValues = CollectPatchValues();

            var missedPatchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType != JsoncContentType.Patch
                    && _patchValues.Any(m =>
                        !string.IsNullOrEmpty(m.Value)
                        && m.Value == n.Value));

            foreach (var item in missedPatchList)
            {
                var plist = _patchValues
                    .Where(m => m.Value == item.Value)
                    .Select(n => n.Key);
                var patches = "";
                foreach (var str in plist)
                {
                    if (!string.IsNullOrEmpty(patches))
                        patches += ", ";

                    patches += str;
                }

                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = item.FullFileName,
                    FileType = item.FileType.ToString(),
                    LineId = item.LineId.ToString(),
                    JsonPath = item.JsonPath,
                    Message = $"Value \"{item.Value}\" can be replaced with patch(es): {patches}",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = "PossiblePatchValues"
                };
                _reportsCollection.Add(report);
            }
        }

        private void PatchAllFields()
        {
            if (_patchValues == null || _patchValues.Count == 0)
                _patchValues = CollectPatchValues();

            var valuesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.Value.Contains('%'));

            if (valuesList.Any())
                foreach (var item in valuesList)
                    foreach (var patch in _patchValues)
                        item.Value = item.Value.Replace(patch.Key, patch.Value);
        }

        private void RedundantPatches()
        {
            var patchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Patch
                    && !n.Shared
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value));
            foreach (var patchResource in patchList)
            {
                if (string.IsNullOrEmpty(patchResource.Value))
                    continue;

                if (!_jsonPropertiesCollection
                    .Any(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType != JsoncContentType.Patch
                        && n.Value.Contains("%" + patchResource.Value + "%")))
                {
                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = patchResource.FullFileName,
                        FileType = patchResource.FileType.ToString(),
                        LineId = patchResource.LineId.ToString(),
                        JsonPath = patchResource.JsonPath,
                        Message = $"Patch \"{patchResource.Value}\" is not used in the project",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = "RedundantPatches"
                    };
                    _reportsCollection.Add(report);
                }
            }
        }

        private void CallNonExistingPatches()
        {
            var patchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Patch
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value));

            foreach (var property in _jsonPropertiesCollection)
            {
                if (property.ItemType != JsonItemType.Property)
                    continue;

                var usedPatchList = GetPatchMacros(property.Value);
                foreach (var patchItem in usedPatchList)
                {
                    if (string.IsNullOrEmpty(patchItem))
                        continue;

                    if (!_systemMacros.Contains(patchItem)
                        && !patchList.Any(n => n.Value == patchItem.Trim('%')))
                    {
                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = property.FullFileName,
                            FileType = property.FileType.ToString(),
                            LineId = property.LineId.ToString(),
                            JsonPath = property.JsonPath,
                            Message = $"Patch \"{patchItem}\" is not defined in the project",
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "CallNonExistingPatches"
                        };
                        _reportsCollection.Add(report);
                    }
                }
            }
        }

        private void OverridingPatches()
        {
            var duplicatePatchesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Patch
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1);

            if (duplicatePatchesList.Any())
                foreach (var dup in duplicatePatchesList)
                {
                    var patchDup = dup.ToArray();
                    for (var i = 1; i < patchDup.Count(); i++)
                    {
                        var oldValue = _jsonPropertiesCollection
                            .LastOrDefault(n => n.FileType == JsoncContentType.Patch
                                                && n.FullFileName == patchDup[i - 1].FullFileName
                                                && n.ParentPath == patchDup[i - 1].ParentPath
                                                && n.Name == "value");
                        if (oldValue.Shared)
                            continue;

                        var newValue = _jsonPropertiesCollection
                            .LastOrDefault(n => n.FileType == JsoncContentType.Patch
                                                && n.FullFileName == patchDup[i].FullFileName
                                                && n.ParentPath == patchDup[i].ParentPath
                                                && n.Name == "value");

                        if (string.IsNullOrEmpty(oldValue.Value) || oldValue.Value == newValue.Value)
                            continue;

                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = patchDup[i - 1].FullFileName
                                           + SplitChar
                                           + patchDup[i].FullFileName,
                            Message = $"Patch \"{patchDup[i - 1].Value}\" is overridden{Environment.NewLine} [\"{oldValue.Value}\" => \"{newValue.Value}\"]",
                            FileType = patchDup[i - 1].FileType
                                       + SplitChar.ToString()
                                       + patchDup[i].FileType,
                            LineId = patchDup[i - 1].LineId
                                     + SplitChar.ToString()
                                     + patchDup[i].LineId,
                            JsonPath = patchDup[i - 1].JsonPath
                                       + SplitChar
                                       + patchDup[i].JsonPath,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "OverridingPatches"
                        };
                        _reportsCollection.Add(report);
                    }
                }
        }

        private void EmptyDataViewNames()
        {
            var emptyDataViewList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.DataViews
                    && !n.Shared
                    && n.Parent == "dataviews"
                    && n.Name == "id"
                    && string.IsNullOrEmpty(n.Value));

            foreach (var viewResource in emptyDataViewList)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = viewResource.FullFileName,
                    FileType = viewResource.FileType.ToString(),
                    LineId = viewResource.LineId.ToString(),
                    JsonPath = viewResource.JsonPath,
                    Message = $"DataView id \"{viewResource.Value}\" is empty",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "EmptyDataViewNames"
                };
                _reportsCollection.Add(report);
            }
        }

        private void RedundantDataViews()
        {
            var dataViewList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.DataViews
                    && !n.Shared
                    && n.Parent == "dataviews"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value));

            foreach (var viewResource in dataViewList)
            {
                if (!_jsonPropertiesCollection
                    .Any(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType != JsoncContentType.DataViews
                        && n.Value.Contains(viewResource.Value)))
                {
                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = viewResource.FullFileName,
                        FileType = viewResource.FileType.ToString(),
                        LineId = viewResource.LineId.ToString(),
                        JsonPath = viewResource.JsonPath,
                        Message = $"DataView \"{viewResource.Value}\" is not used in the project",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = "RedundantDataViews"
                    };
                    _reportsCollection.Add(report);
                }
            }
        }

        private void CallNonExistingDataViews()
        {
            var dataViewList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.DataViews
                    && n.Parent == "dataviews"
                    && n.Name == "id"
                    || n.FileType == JsoncContentType.Events
                    && n.Parent == "param"
                    && n.Name == "result"
                    && !string.IsNullOrEmpty(n.Value));

            foreach (var property in _jsonPropertiesCollection)
            {
                if (property.ItemType != JsonItemType.Property)
                    continue;

                var usedDataViewsList = new List<string>();

                if (property.FileType == JsoncContentType.Events
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
                    usedDataViewsList = GetValueCall(property.Value);
                }
                else if (property.FileType == JsoncContentType.Events
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
                        usedDataViewsList = GetValueCall(property.Value);
                    else
                        usedDataViewsList.Add(property.Value + ".");
                }
                else if (!string.IsNullOrEmpty(property.Value))
                {
                    usedDataViewsList = GetValueCall(property.Value);
                }

                foreach (var dataViewItem in usedDataViewsList)
                {
                    if (string.IsNullOrEmpty(dataViewItem))
                        continue;

                    var viewName = dataViewItem.Substring(0, dataViewItem.IndexOf('.'));
                    if (!_systemDataViews.Contains(viewName)
                        && !viewName.StartsWith("%")
                        && !dataViewList.Any(n => n.Value == viewName))
                    {
                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = property.FullFileName,
                            FileType = property.FileType.ToString(),
                            LineId = property.LineId.ToString(),
                            JsonPath = property.JsonPath,
                            Message = $"DataView \"{viewName}\" is not defined in the project",
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "CallNonExistingDataViews"
                        };
                        _reportsCollection.Add(report);
                    }
                }
            }
        }

        private void CallNonExistingDataTables()
        {
            var dataTableList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.DataViews
                    && n.Parent == "dataviews"
                    && n.Name == "table"
                    && !string.IsNullOrEmpty(n.Value)).Select(n => n.Value).Distinct();

            foreach (var property in _jsonPropertiesCollection)
            {
                if (property.ItemType != JsonItemType.Property)
                    continue;

                var usedDataTable = "";

                if (property.FileType == JsoncContentType.Events
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
                {
                    usedDataTable = property.Value;
                }

                if (string.IsNullOrEmpty(usedDataTable))
                    continue;

                if (!usedDataTable.StartsWith("%")
                    && !dataTableList.Contains(usedDataTable))
                {
                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = property.FullFileName,
                        FileType = property.FileType.ToString(),
                        LineId = property.LineId.ToString(),
                        JsonPath = property.JsonPath,
                        Message = $"DataTable \"{usedDataTable}\" is not defined in the project",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "CallNonExistingDataTables"
                    };
                    _reportsCollection.Add(report);
                }
            }
        }

        private void OverridingDataViews()
        {
            var duplicateViewsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.DataViews
                    && n.Parent == "dataviews"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1);

            if (duplicateViewsList.Any())
                foreach (var dup in duplicateViewsList)
                {
                    var viewDup = dup.ToArray();
                    for (var i = 1; i < viewDup.Count(); i++)
                    {
                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = viewDup[i - 1].FullFileName
                                           + SplitChar
                                           + viewDup[i].FullFileName,
                            Message = $"DataView \"{viewDup[i - 1].Value}\" is overridden",
                            FileType = viewDup[i - 1].FileType
                                       + SplitChar.ToString()
                                       + viewDup[i].FileType,
                            LineId = viewDup[i - 1].LineId
                                     + SplitChar.ToString()
                                     + viewDup[i].LineId,
                            JsonPath = viewDup[i - 1].JsonPath
                                       + SplitChar
                                       + viewDup[i].JsonPath,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "OverridingDataViews"
                        };
                        _reportsCollection.Add(report);
                    }
                }
        }

        private void EmptyRuleNames()
        {
            var emptyRulesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Rules
                    && n.Parent == "rules"
                    && n.Name == "id"
                    && string.IsNullOrEmpty(n.Value));

            foreach (var dup in emptyRulesList)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = dup.FullFileName,
                    Message = $"Rule id \"{dup.Value}\" is empty",
                    FileType = dup.FileType.ToString(),
                    LineId = dup.LineId.ToString(),
                    JsonPath = dup.JsonPath,
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "EmptyRuleNames"
                };
                _reportsCollection.Add(report);
            }
        }

        private void OverridingRules()
        {
            var duplicateRulesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Rules
                    && n.Parent == "rules"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1);

            if (duplicateRulesList.Any())
            {
                foreach (var dup in duplicateRulesList)
                {
                    var ruleDup = dup.ToArray();
                    for (var i = 1; i < ruleDup.Count(); i++)
                    {
                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = ruleDup[i - 1].FullFileName
                                           + SplitChar
                                           + ruleDup[i].FullFileName,
                            Message = $"Rule \"{ruleDup[i - 1].Value}\" is overridden",
                            FileType = ruleDup[i - 1].FileType
                                       + SplitChar.ToString()
                                       + ruleDup[i].FileType,
                            LineId = ruleDup[i - 1].LineId
                                     + SplitChar.ToString()
                                     + ruleDup[i].LineId,
                            JsonPath = ruleDup[i - 1].JsonPath
                                       + SplitChar
                                       + ruleDup[i].JsonPath,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "OverridingRules"
                        };
                        _reportsCollection.Add(report);
                    }
                }
            }
        }

        private void EmptyToolNames()
        {
            var emptyToolsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Tools
                    && n.Parent == "tools"
                    && n.Name == "id"
                    && string.IsNullOrEmpty(n.Value));

            foreach (var dup in emptyToolsList)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = dup.FullFileName,
                    Message = $"Tool id \"{dup.Value}\" is empty",
                    FileType = dup.FileType.ToString(),
                    LineId = dup.LineId.ToString(),
                    JsonPath = dup.JsonPath,
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "EmptyToolNames"
                };
                _reportsCollection.Add(report);
            }
        }

        private void OverridingTools()
        {
            var duplicateToolsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Tools
                    && n.Parent == "tools"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1);

            if (duplicateToolsList.Any())
                foreach (var dup in duplicateToolsList)
                {
                    var toolDup = dup.ToArray();
                    for (var i = 1; i < toolDup.Count(); i++)
                    {
                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = toolDup[i - 1].FullFileName
                                           + SplitChar
                                           + toolDup[i].FullFileName,
                            Message = $"Tool \"{toolDup[i - 1].Value}\" is overridden",
                            FileType = toolDup[i - 1].FileType
                                       + SplitChar.ToString()
                                       + toolDup[i].FileType,
                            LineId = toolDup[i - 1].LineId
                                     + SplitChar.ToString()
                                     + toolDup[i].LineId,
                            JsonPath = toolDup[i - 1].JsonPath
                                       + SplitChar
                                       + toolDup[i].JsonPath,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "OverridingTools"
                        };
                        _reportsCollection.Add(report);
                    }
                }
        }

        private void MissingSearches()
        {
            var searchesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "like"
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Parent == "searchOptions"
                    && _jsonPropertiesCollection.Any(m =>
                        m.ItemType == JsonItemType.Property
                        && m.Name == "searchForm"
                        && m.FullFileName == n.FullFileName
                        && m.FileType == JsoncContentType.Events
                        && m.ParentPath == n.ParentPath));

            foreach (var form in searchesList)
            {
                var formSubFolder = _jsonPropertiesCollection
                    .Where(m =>
                        m.ItemType == JsonItemType.Property
                        && m.Name == "searchForm"
                        && !string.IsNullOrEmpty(m.Value)
                        && m.FullFileName == form.FullFileName
                        && m.FileType == JsoncContentType.Events
                        && m.ParentPath == form.ParentPath);

                if (!formSubFolder.Any())
                    continue;

                var searchName = form.Value
                    + "\\"
                    + formSubFolder.FirstOrDefault().Value;
                var searchFile = "";
                if (_isDeploymentFolder)
                {
                    searchFile = _projectPath
                        + "\\..\\Shared\\search\\"
                        + searchName
                        + "\\search.jsonc";
                }
                else
                {
                    if (_isIceFolder)
                        searchFile = _projectPath
                            + "\\..\\..\\..\\Shared\\search\\"
                            + searchName
                            + "\\search.jsonc";
                    else
                        searchFile = _projectPath
                            + "\\..\\..\\Shared\\search\\"
                            + searchName
                            + "\\search.jsonc";
                }

                if (!File.Exists(searchFile))
                {
                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = form.FullFileName,
                        Message = $"Call to non-existing search \"{searchName}\"",
                        FileType = form.FileType.ToString(),
                        LineId = form.LineId.ToString(),
                        JsonPath = form.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "MissingSearches"
                    };
                    _reportsCollection.Add(report);
                }
            }
        }

        private void MissingForms()
        {
            var formsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "view"
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Parent == "param"
                    && _jsonPropertiesCollection.Any(m =>
                        n.ItemType == JsonItemType.Property
                        && m.Name == "type"
                        && m.Value == "app-open"
                        && m.FullFileName == n.FullFileName
                        && m.FileType == JsoncContentType.Events
                        && m.ParentPath + ".param" == n.ParentPath));

            foreach (var form in formsList)
            {
                List<string> formFileList;
                var fileFound = false;

                if (_isDeploymentFolder)
                {
                    formFileList = new List<string>
                    {
                        _projectPath + "\\..\\" + form.Value + "\\events.jsonc"
                    };
                }
                else
                {
                    if (_isIceFolder)
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
                    else
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
                }

                foreach (var formFile in formFileList)
                {
                    if (File.Exists(formFile))
                    {
                        fileFound = true;
                        break;
                    }
                }

                if (!fileFound)
                {
                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = form.FullFileName,
                        Message = $"Call to non-existing form \"{form.Value}\" (Could it be a WinForm?)",
                        FileType = form.FileType.ToString(),
                        LineId = form.LineId.ToString(),
                        JsonPath = form.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = "MissingForms"
                    };
                    _reportsCollection.Add(report);
                }
            }
        }

        private void JsCode()
        {
            var jsPatternsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.Value.Contains("#_"));

            foreach (var dup in jsPatternsList)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = dup.FullFileName,
                    Message = $"Property value contains JS code \"{dup.Value}\"",
                    FileType = dup.FileType.ToString(),
                    LineId = dup.LineId.ToString(),
                    JsonPath = dup.JsonPath,
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = "JsCode"
                };
                _reportsCollection.Add(report);
            }
        }

        private void JsDataViewCount()
        {
            var jsPatternsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.Value.Contains("#_trans.dataView(")
                    && n.Value.Contains(").count"));

            foreach (var dup in jsPatternsList)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = dup.FullFileName,
                    Message = $"JS code \"{dup.Value}\" must be replaced to \"%DataView.count%\"",
                    FileType = dup.FileType.ToString(),
                    LineId = dup.LineId.ToString(),
                    JsonPath = dup.JsonPath,
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Warning.ToString(),
                    Source = "JsDataViewCount"
                };
                _reportsCollection.Add(report);
            }
        }

        private void MissingLayoutIds()
        {
            var layoutIdList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Layout
                    && n.JsonDepth == 2
                    && n.Name == "id");

            foreach (var idItem in layoutIdList)
            {
                var modelId = _jsonPropertiesCollection
                    .Where(n =>
                        n.FullFileName == idItem.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Layout
                        && n.ParentPath == idItem.ParentPath + ".model"
                        && n.Name == "id").ToArray();

                var report = new ReportItem();
                if (!modelId.Any())
                {
                    report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = idItem.FullFileName,
                        Message = $"Layout control id=\"{idItem.Value}\" has no model id",
                        FileType = idItem.FileType.ToString(),
                        LineId = idItem.LineId.ToString(),
                        JsonPath = idItem.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "MissingLayoutIds"
                    };
                }
                else
                {
                    continue;
                }

                _reportsCollection.Add(report);
            }
        }

        private void IncorrectLayoutIds()
        {
            var layoutIdList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Layout
                    && n.JsonDepth == 2
                    && n.Name == "id");

            foreach (var idItem in layoutIdList)
            {
                var modelId = _jsonPropertiesCollection
                    .Where(n =>
                        n.FullFileName == idItem.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Layout
                        && n.ParentPath == idItem.ParentPath + ".model"
                        && n.Name == "id").ToArray();

                var report = new ReportItem();
                if (!modelId.Any())
                {
                    continue;
                }
                else if (idItem.Value != modelId.First().Value)
                {
                    report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = idItem.FullFileName
                                       + SplitChar
                                       + modelId.First().FullFileName,
                        Message = $"Layout control id=\"{idItem.Value}\" doesn't match model id=\"{modelId.First().Value}\"",
                        FileType = idItem.FileType.ToString()
                                   + SplitChar
                                   + modelId.First().FileType.ToString(),
                        LineId = idItem.LineId.ToString()
                                 + SplitChar
                                 + modelId.First().LineId.ToString(),
                        JsonPath = idItem.JsonPath
                                   + SplitChar
                                   + modelId.First().JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "IncorrectLayoutIds"
                    };
                }
                else
                {
                    continue;
                }

                _reportsCollection.Add(report);
            }
        }

        //private void IncorrectDVContitionViewName([CallerMemberName] string methodName = null)
        private void IncorrectDVContitionViewName()
        {
            var dvConditionsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "type"
                    && n.Value == "dataview-condition");

            foreach (var item in dvConditionsList)
            {
                var dvName = _jsonPropertiesCollection
                    .Where(n =>
                        n.FullFileName == item.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Events
                        && n.ParentPath == item.ParentPath + ".param"
                        && n.Name == "dataview").ToArray();

                var resultDvName = _jsonPropertiesCollection
                    .Where(n =>
                        n.FullFileName == item.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Events
                        && n.ParentPath == item.ParentPath + ".param"
                        && n.Name == "result").ToArray();

                if (dvName.Length != 1 || resultDvName.Length != 1)
                {
                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = $"Incorrect dataview-condition definition",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "IncorrectDVContitionViewName"
                    };
                    _reportsCollection.Add(report);
                }
                if (dvName[0].Value == resultDvName[0].Value)
                {
                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = $"Incorrect dataview-condition definition: \"dataview\" should be different from \"result\"",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = dvName[0].JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "IncorrectDVContitionViewName"
                    };
                    _reportsCollection.Add(report);
                }
            }
        }

        private void IncorrectTabIds()
        {
            var tabIdsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Layout
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Name == "tabId").Select(n => n.Value).ToArray();

            var tabStripsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Layout
                    && n.Name == "sourceTypeId"
                    && n.Value == "metafx-tabstrip");

            foreach (var tab in tabStripsList)
            {
                var tabsList = _jsonPropertiesCollection
                    .Where(n =>
                        !n.Shared
                        && n.FullFileName == tab.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Layout
                        && n.JsonPath.Contains(tab.ParentPath + ".model.data")
                        && n.Name == "page"
                        && !string.IsNullOrEmpty(n.Value));

                foreach (var item in tabsList)
                {
                    if (!tabIdsList.Contains(item.Value))
                    {
                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = item.FullFileName,
                            Message = $"Inexistent tab link: {item.Value}",
                            FileType = item.FileType.ToString(),
                            LineId = item.LineId.ToString(),
                            JsonPath = item.JsonPath,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "IncorrectTabIds"
                        };
                        _reportsCollection.Add(report);
                    }
                }
            }
        }

        // misprints in property/dataview/string/id names (try finding lower-case property in schema or project scope)
        // searches must use only one dataview

    }
}
