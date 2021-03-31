using System;
using System.Collections.Generic;
using System.Text;

using NJsonSchema.Validation;

namespace KineticValidator
{
    internal enum FolderType
    {
        Unknown,
        Deployment,
        Repository,
        IceRepository,
    }
    internal class ContentTypeItem
    {
        public string FileTypeMask;
        public string PropertyTypeName;
        public JsoncContentType FileType;

        public ContentTypeItem()
        {
            FileTypeMask = "";
            PropertyTypeName = "";
            FileType = JsoncContentType.Unknown;
        }
    }

    internal class ProcessConfiguration
    {
        public char SplitChar;
        public string SchemaTag;
        public string BackupSchemaExtension;
        public string FileMask;
        public List<ContentTypeItem> FileTypes;
        public bool IgnoreHttpsError;
        public bool SkipSchemaErrors;
        public string[] SystemMacros;
        public string[] SystemDataViews;
        public List<ValidationErrorKind> SuppressSchemaErrors;
    }

    internal class ProjectConfiguration
    {
        public string ProjectName;
        public string ProjectPath;
        public FolderType FolderType;
    }

    internal class SeedData
    {
        public Dictionary<string, string> ProcessedFilesList;
        public List<JsonProperty> JsonPropertiesCollection;
        public List<ReportItem> RunValidationReportsCollection;
        public List<ReportItem> DeserializeFileReportsCollection;
        public List<ReportItem> ParseJsonObjectReportsCollection;
    }

    internal static class Utilities
    {
        internal static string ExceptionPrint(Exception ex)
        {
            var exceptionMessage = new StringBuilder();

            exceptionMessage.AppendLine(ex.Message);
            if (ex.InnerException != null)
                exceptionMessage.AppendLine(ExceptionPrint(ex.InnerException));

            return exceptionMessage.ToString();
        }

        internal static bool IsShared(string fullFileName, string projectPath)
        {
            return !fullFileName.Contains(projectPath);
        }

        internal static JsoncContentType GetFileTypeFromFileName(string fullFileName, List<ContentTypeItem> _fileTypes)
        {
            var fileType = JsoncContentType.Unknown;
            var shortFileName = GetShortFileName(fullFileName);
            foreach (var item in _fileTypes)
                if (shortFileName.EndsWith(item.FileTypeMask))
                {
                    fileType = item.FileType;
                    break;
                }

            return fileType;
        }

        internal static string GetShortFileName(string longFileName)
        {
            if (string.IsNullOrEmpty(longFileName))
            {
                return longFileName;
            }

            var i = longFileName.LastIndexOf('\\');
            if (i < 0)
            {
                return longFileName;
            }

            if (i + 1 >= 0 && longFileName.Length > i + 1)
                return i < 0 ? longFileName : longFileName.Substring(i + 1);
            return longFileName;
        }
    }
}
