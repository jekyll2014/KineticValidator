using System;
using System.Runtime.Serialization;
using System.Text;

namespace KineticValidator
{
    [DataContract]
    [Serializable]
    public enum ImportanceEnum
    {
        [EnumMember] Note,
        [EnumMember] Warning,
        [EnumMember] Error
    }

    [DataContract]
    [Serializable]
    public enum ValidationTypeEnum
    {
        [EnumMember] None,
        [EnumMember] File,
        [EnumMember] Scheme,
        [EnumMember] Logic,
        [EnumMember] Parse
    }

    [DataContract]
    [Serializable]
    public enum ReportColumns
    {
        [DataMember] ProjectName,
        [DataMember] LineId,
        [DataMember] FileType,
        [DataMember] Message,
        [DataMember] JsonPath,
        [DataMember] ValidationType,
        [DataMember] Source,
        [DataMember] Severity,
        [DataMember] FullFileName,
    }

    [DataContract]
    [Serializable]
    internal class ReportItem
    {
        [DataMember] public string ProjectName;
        [DataMember] public string FullFileName;

        public string ShortFileName
        {
            get
            {
                var i = FullFileName.LastIndexOf('\\');
                if (i >= FullFileName.Length)
                    return "";
                return i < 0 ? FullFileName : FullFileName.Substring(i + 1);
            }
        }

        [DataMember] public string FileType;
        [DataMember] public string Message;
        [DataMember] public string LineId;
        [DataMember] public string JsonPath;
        [DataMember] public string ValidationType;
        [DataMember] public string Severity;
        [DataMember] public string Source;

        public ReportItem()
        {
            ProjectName = "";
            FullFileName = "";
            FileType = JsoncContentType.Unknown.ToString();
            Message = "";
            LineId = "";
            JsonPath = "";
            Severity = ImportanceEnum.Note.ToString();
            ValidationType = ValidationTypeEnum.None.ToString();
            Source = "";
        }

        public string ToText()
        {
            var text = new StringBuilder();
            text.AppendLine("{");
            text.AppendLine("\tProjectName: " + ProjectName);
            text.AppendLine("\tFullFileName: " + FullFileName);
            text.AppendLine("\tFileType: " + FileType);
            text.AppendLine("\tMessage: " + Message);
            text.AppendLine("\tLineId: " + LineId);
            text.AppendLine("\tJsonPath: " + JsonPath);
            text.AppendLine("\tImportance: " + Severity);
            text.AppendLine("\tValidationType: " + ValidationType);
            text.AppendLine("\tSource: " + Source);
            text.Append("}");
            return text.ToString();
        }

        public bool Equals(ReportItem item)
        {
            return (FileType == item.FileType || string.IsNullOrEmpty(FileType) || string.IsNullOrEmpty(item.FileType))
                   && (FullFileName == item.FullFileName || string.IsNullOrEmpty(FullFileName) || string.IsNullOrEmpty(item.FullFileName))
                   && (Message == item.Message || string.IsNullOrEmpty(Message) || string.IsNullOrEmpty(item.Message))
                   && (JsonPath == item.JsonPath || string.IsNullOrEmpty(JsonPath) || string.IsNullOrEmpty(item.JsonPath))
                   && (ValidationType == item.ValidationType || string.IsNullOrEmpty(ValidationType) || string.IsNullOrEmpty(item.ValidationType))
                   && (Severity == item.Severity || string.IsNullOrEmpty(Severity) || string.IsNullOrEmpty(item.Severity));
        }
    }
}
