﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NJsonSchema.Validation;

namespace KineticValidator
{
    internal enum FolderType
    {
        Unknown,
        Deployment,
        Repository,
        IceRepository
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
        public bool PatchAllFields;
        public string[] SystemMacros;
        public string[] SystemDataViews;
        public List<ValidationErrorKind> SuppressSchemaErrors;
        public string ServerAssembliesPath;
    }

    internal class ProjectConfiguration
    {
        public string ProjectName;
        public string ProjectPath;
        public FolderType FolderType;
    }

    internal class SeedData
    {
        public ConcurrentDictionary<string, string> ProcessedFilesList;
        public IEnumerable<JsonProperty> JsonPropertiesCollection;
        public IEnumerable<ReportItem> RunValidationReportsCollection;
        public IEnumerable<ReportItem> DeserializeFileReportsCollection;
        public IEnumerable<ReportItem> ParseJsonObjectReportsCollection;
    }

    internal static class Utilities
    {
        internal static string ExceptionPrint(Exception ex)
        {
            var exceptionMessage = new StringBuilder();

            exceptionMessage.AppendLine(ex.Message);
            if (ex.InnerException != null)
            {
                exceptionMessage.AppendLine(ExceptionPrint(ex.InnerException));
            }

            return exceptionMessage.ToString();
        }

        internal static bool IsShared(string fullFileName, string projectPath)
        {
            return !fullFileName.Contains(projectPath);
        }

        internal static JsoncContentType GetFileTypeFromFileName(string fullFileName,
            IEnumerable<ContentTypeItem> _fileTypes)
        {
            var shortFileName = GetShortFileName(fullFileName);

            return (from item in _fileTypes where shortFileName.EndsWith(item.FileTypeMask) select item.FileType).FirstOrDefault();
        }

        internal static string GetShortFileName(string longFileName)
        {
            if (string.IsNullOrEmpty(longFileName)) return longFileName;

            var i = longFileName.LastIndexOf('\\');
            if (i < 0) return longFileName;

            if (i + 1 >= 0 && longFileName.Length > i + 1)
            {
                return longFileName.Substring(i + 1);
            }

            return longFileName;
        }

        private static readonly object lockDevLogFile = new object();

        internal static void SaveDevLog(string text)
        {
            lock (lockDevLogFile)
            {
                try
                {
                    File.AppendAllText("DeveloperLog.txt", text + Environment.NewLine);
                }
                catch
                {
                }
            }
        }
    }
}
