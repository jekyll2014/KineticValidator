using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using KineticValidator.Properties;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using NJsonSchema.Validation;

using static KineticValidator.JsonPathParser;

namespace KineticValidator
{
    public partial class MainForm : Form
    {
        // predefined constants
        private readonly List<ValidationErrorKind> _suppressSchemaErrors;

        private readonly string[] _initialProjectFiles;
        private readonly string[] _systemMacros;
        private readonly string[] _systemDataViews;
        private string _serverAssembliesPath;

        private const string FileMask = "*.jsonc";
        private const string SchemaTag = "\"$schema\"";
        private const string BackupSchemaExtension = ".original";

        private const string VersionTagName = "contentVersion";
        private const string SchemaTagName = "$schema";
        private const string FileTagName = "$ref";
        private const string ImportTagName = "imports";

        private const string LogFileName = "hiddenerrors.log";
        private const string DefaultFormCaption = "KineticValidator";
        private const string IgnoreFileName = "ignore.json";
        private const string GlobalIgnoreFileName = "ignore.json";
        private const string ReportTxtFileName = "report.txt";
        private const string ReportJsonFileName = "report.json";
        private const char SplitChar = ';';

        private static readonly string HelpString = "Usage: KineticValidator.exe [/i] [/s] [/t] [/h] projectDir"
            + Environment.NewLine
            + "/i - ignore HTTPS errors (insecure)"
            + Environment.NewLine
            + "/s - skip most annoying schema validation errors"
            + Environment.NewLine
            + "/t - save temporary project files"
            + Environment.NewLine
            + "/h, /? - get help"
            + Environment.NewLine;

        private readonly List<ContentTypeItem> _fileTypes = new List<ContentTypeItem>
        {
            new ContentTypeItem
            {
                FileTypeMask = "dataviews.jsonc",
                PropertyTypeName = "dataviews",
                FileType = JsoncContentType.DataViews
            },
            new ContentTypeItem
            {
                FileTypeMask = "events.jsonc",
                PropertyTypeName = "events",
                FileType = JsoncContentType.Events},
            new ContentTypeItem
            {
                FileTypeMask = "layout.jsonc",
                PropertyTypeName = "layout",
                FileType = JsoncContentType.Layout},
            new ContentTypeItem
            {
                FileTypeMask = "rules.jsonc",
                PropertyTypeName = "rules",
                FileType = JsoncContentType.Rules},
            new ContentTypeItem
            {
                FileTypeMask = "search.jsonc",
                PropertyTypeName = "search",
                FileType = JsoncContentType.Search},
            new ContentTypeItem
            {
                FileTypeMask = "combo.jsonc",
                PropertyTypeName = "combo",
                FileType = JsoncContentType.Combo},
            new ContentTypeItem
            {
                FileTypeMask = "tools.jsonc",
                PropertyTypeName = "tools",
                FileType = JsoncContentType.Tools},
            new ContentTypeItem
            {
                FileTypeMask = "strings.jsonc",
                PropertyTypeName = "strings",
                FileType = JsoncContentType.Strings},
            new ContentTypeItem
            {
                FileTypeMask = "patch.jsonc",
                PropertyTypeName = "patch",
                FileType = JsoncContentType.Patch}
        };

        // behavior options
        private bool _ignoreHttpsError;
        private bool _alwaysOnTop;
        private bool _reformatJson;
        private bool _skipSchemaErrors;
        private bool _runFromCmdLine;
        private bool _saveTmpFiles;
        private bool _saveReport;
        private bool _isCollectionFolder;
        private bool _showPreview;
        private FolderType _folderType;

        private struct WinPosition
        {
            public int WinX;
            public int WinY;
            public int WinW;
            public int WinH;
        }

        WinPosition[] _editorPosition = new WinPosition[2];

        // global variables
        private readonly StringBuilder _textLog = new StringBuilder();
        private string _projectPath = "";
        private string _projectName = "";
        private int _oldColumn = -1;
        private int _oldRow = -1;

        //schema URL, schema text
        private List<string> _filesList = new List<string>();
        private List<JsonProperty> _jsonPropertiesCollection = new List<JsonProperty>();
        private List<ReportItem> _RunValidationReportsCollection = new List<ReportItem>();
        private List<ReportItem> _DeserializeFileReportsCollection = new List<ReportItem>();
        private List<ReportItem> _ParseJsonObjectReportsCollection = new List<ReportItem>();
        private List<ReportItem> _ignoreReportsCollection = new List<ReportItem>();

        // full file name, schema URL
        private Dictionary<string, string> _processedFilesList = new Dictionary<string, string>();
        private DataTable _reportTable = new DataTable();
        private Dictionary<string, Func<string, List<ReportItem>>> _validatorsList = new Dictionary<string, Func<string, List<ReportItem>>>();
        private List<string> _checkedValidators = new List<string>();

        JsonViewer[] _editors = new JsonViewer[2];

        private string MainFormCaption
        {
            get => base.Text;
            set => base.Text = value;
        }

        #region GUI

        public MainForm(string[] args)
        {
            _initialProjectFiles = Settings.Default.InitialProjectFiles.Split(SplitChar);
            _systemMacros = Settings.Default.SystemMacros.Split(SplitChar);
            _systemDataViews = Settings.Default.SystemDataViews.Split(SplitChar);
            _serverAssembliesPath = Settings.Default.ServerAssembliesPath;

            _ignoreHttpsError = Settings.Default.IgnoreHttpsError;
            _skipSchemaErrors = Settings.Default.SkipSchemaErrors;
            _saveTmpFiles = Settings.Default.SaveTmpFiles;
            _saveReport = Settings.Default.SaveReport;

            _suppressSchemaErrors = new List<ValidationErrorKind>();
            var annoyingErrors = Settings.Default.IgnoreSchemaErrors.Split(SplitChar);
            foreach (var error in annoyingErrors)
            {
                if (Enum.TryParse(error, out ValidationErrorKind errKind))
                {
                    _suppressSchemaErrors.Add(errKind);
                }
            }

            _validatorsList = ProjectValidator._validatorsList;

            _checkedValidators = Settings.Default.EnabledValidators.Trim(SplitChar).Split(SplitChar).ToList();

            if (args.Length > 0)
            {
                RunCommandLine(args);
                return;
            }

            InitializeComponent();

            _editorPosition[0].WinX = Settings.Default.Editor1PositionX;
            _editorPosition[0].WinY = Settings.Default.Editor1PositionY;
            _editorPosition[0].WinW = Settings.Default.Editor1Width;
            _editorPosition[0].WinH = Settings.Default.Editor1Height;
            _editorPosition[1].WinX = Settings.Default.Editor2PositionX;
            _editorPosition[1].WinY = Settings.Default.Editor2PositionY;
            _editorPosition[1].WinW = Settings.Default.Editor2Width;
            _editorPosition[1].WinH = Settings.Default.Editor2Height;

            MainFormCaption = DefaultFormCaption;
            checkBox_ignoreHttpsError.Checked = _ignoreHttpsError;
            checkBox_skipSchemaProblems.Checked = _skipSchemaErrors;
            checkBox_saveFiles.Checked = _saveTmpFiles;
            checkBox_saveReport.Checked = _saveReport;
            checkBox_alwaysOnTop.Checked = TopMost = _alwaysOnTop = Settings.Default.AlwaysOnTop;
            checkBox_reformatJson.Checked = _reformatJson = Settings.Default.ReformatJson;
            checkBox_showPreview.Checked = _showPreview = Settings.Default.ShowPreview;

            foreach (var validator in _validatorsList)
            {
                var checkedValidator = _checkedValidators.Contains(validator.Value.Method.Name);
                checkedListBox_validators.Items.Add(validator.Key, checkedValidator);
            }

            var WinX = Settings.Default.MainWindowPositionX;
            var WinY = Settings.Default.MainWindowPositionY;
            var WinW = Settings.Default.MainWindowWidth;
            var WinH = Settings.Default.MainWindowHeight;

            if (!(WinX == 0 && WinY == 0 && WinW == 0 && WinH == 0))
            {
                this.Location = new Point
                { X = WinX, Y = WinY };
                this.Width = WinW;
                this.Height = WinH;
            }

            SetProject(Settings.Default.LastProjectFolder);
            ActivateUiControls(true);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.dataGridView_report.SelectionChanged -= new EventHandler(this.DataGridView_report_SelectionChanged);

            Settings.Default.ServerAssembliesPath = _serverAssembliesPath;
            Settings.Default.LastProjectFolder = _projectPath;
            Settings.Default.IgnoreHttpsError = _ignoreHttpsError;
            Settings.Default.AlwaysOnTop = _alwaysOnTop;
            Settings.Default.SkipSchemaErrors = _skipSchemaErrors;
            Settings.Default.ReformatJson = _reformatJson;
            Settings.Default.ShowPreview = _showPreview;
            Settings.Default.SaveTmpFiles = _saveTmpFiles;
            Settings.Default.SaveReport = _saveReport;
            Settings.Default.MainWindowPositionX = this.Location.X;
            Settings.Default.MainWindowPositionY = this.Location.Y;
            Settings.Default.MainWindowWidth = this.Width;
            Settings.Default.MainWindowHeight = this.Height;

            for (var i = 0; i < 2; i++)
            {
                if (_editors[i] != null && !_editors[i].IsDisposed)
                {
                    _editorPosition[i].WinX = _editors[i].Location.X;
                    _editorPosition[i].WinY = _editors[i].Location.Y;
                    _editorPosition[i].WinW = _editors[i].Width;
                    _editorPosition[i].WinH = _editors[i].Height;
                }
            }

            Settings.Default.Editor1PositionX = _editorPosition[0].WinX;
            Settings.Default.Editor1PositionY = _editorPosition[0].WinY;
            Settings.Default.Editor1Width = _editorPosition[0].WinW;
            Settings.Default.Editor1Height = _editorPosition[0].WinH;
            Settings.Default.Editor2PositionX = _editorPosition[1].WinX;
            Settings.Default.Editor2PositionY = _editorPosition[1].WinY;
            Settings.Default.Editor2Width = _editorPosition[1].WinW;
            Settings.Default.Editor2Height = _editorPosition[1].WinH;

            Settings.Default.MessageColumnWidth = dataGridView_report.Columns[ReportColumns.Message.ToString()].Width;

            var enabledValidators = new StringBuilder();
            foreach (var validator in checkedListBox_validators.CheckedItems)
            {
                if (_validatorsList.TryGetValue(validator.ToString(), out var v))
                {
                    enabledValidators.Append(v.Method.Name + SplitChar);
                }
            }
            Settings.Default.EnabledValidators = enabledValidators.ToString();

            Settings.Default.Save();
        }

        private void Button_selectFiles_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.SelectedPath = _projectPath;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK &&
                !string.IsNullOrEmpty(folderBrowserDialog1.SelectedPath))
            {
                SetProject(folderBrowserDialog1.SelectedPath);
                ActivateUiControls(true);
            }
        }

        private async void Button_validateProject_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_projectPath))
                return;

            ActivateUiControls(false);

            _filesList = new List<string>();
            _jsonPropertiesCollection = new List<JsonProperty>();
            _RunValidationReportsCollection = new List<ReportItem>();
            _DeserializeFileReportsCollection = new List<ReportItem>();
            _ParseJsonObjectReportsCollection = new List<ReportItem>();
            var reportsCollection = new List<ReportItem>();
            _ignoreReportsCollection = new List<ReportItem>();
            _processedFilesList = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);


            if (!_runFromCmdLine)
            {
                InitReportsGrid();
                await Task.Run(() =>
                {
                    reportsCollection = RunValidation(true, _saveTmpFiles);
                    ProcessReport(reportsCollection, _saveReport, true);
                }).ConfigureAwait(true);
            }
            else
            {
                reportsCollection = RunValidation(true, _saveTmpFiles);
                ProcessReport(reportsCollection, _saveReport, true);
            }

            FlushLog();

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            SetStatus("");

            if (!_runFromCmdLine)
            {
                tabControl1.SelectTab(1);
            }

            ActivateUiControls(true);
        }

        private async void Button_validateAll_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_projectPath))
                return;

            ActivateUiControls(false);
            InitReportsGrid(true);

            var collectionFoler = _projectPath;

            var totalReportsCollection = new List<ReportItem>();
            var dirList = new List<string>();
            try
            {
                dirList.AddRange(Directory.GetDirectories(collectionFoler,
                    "*.*",
                    SearchOption.TopDirectoryOnly));
            }
            catch (Exception ex)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    Message = "Folders search error" + ex,
                    ValidationType = ValidationTypeEnum.File.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "Button_validateAll_Click"
                };
                totalReportsCollection.Add(report);
            }

            // do not process 'Shared' on deployment folder processing
            var sharedDir = dirList.Where(n => n.EndsWith("\\Shared"));
            if (sharedDir.Any())
            {
                dirList.Remove(sharedDir.First());
            }

            foreach (var dir in dirList)
            {
                SetProject(dir, false);

                _filesList = new List<string>();
                _jsonPropertiesCollection = new List<JsonProperty>();
                _ignoreReportsCollection = new List<ReportItem>();
                _processedFilesList = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _RunValidationReportsCollection = new List<ReportItem>();
                _DeserializeFileReportsCollection = new List<ReportItem>();
                _ParseJsonObjectReportsCollection = new List<ReportItem>();
                var reportsCollection = new List<ReportItem>();
                InitValidator(true);

                if (!_runFromCmdLine)
                {
                    await Task.Run(() =>
                {
                    reportsCollection = RunValidation(false, _saveTmpFiles);
                    ProcessReport(reportsCollection, _saveReport, false);
                    totalReportsCollection.AddRange(reportsCollection);
                }).ConfigureAwait(true);
                }
                else
                {
                    reportsCollection = RunValidation(false, _saveTmpFiles);
                    ProcessReport(reportsCollection, _saveReport, false);
                    totalReportsCollection.AddRange(reportsCollection);
                }
                FlushLog();

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                SetStatus("");
            }

            SetProject(collectionFoler, false);
            InitReportsGrid(_isCollectionFolder);
            ProcessReport(totalReportsCollection, _saveReport, true);
            tabControl1.SelectTab(1);
            ActivateUiControls(true);
        }

        private void CheckBox_ignoreHttpsError_CheckedChanged(object sender, EventArgs e)
        {
            _ignoreHttpsError = checkBox_ignoreHttpsError.Checked;
        }

        private void CheckBox_alwaysOnTop_CheckedChanged(object sender, EventArgs e)
        {
            _alwaysOnTop = checkBox_alwaysOnTop.Checked;
            TopMost = _alwaysOnTop;
        }

        private void CheckBox_skipSchemaProblems_CheckedChanged(object sender, EventArgs e)
        {
            _skipSchemaErrors = checkBox_skipSchemaProblems.Checked;
        }

        private void DataGridView_report_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && (e.KeyCode == Keys.C || e.KeyCode == Keys.Insert))
                Clipboard.SetText(dataGridView_report.CurrentCell.Value.ToString());
        }

        private void DataGridView_report_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (!(sender is DataGridView dataGrid) || e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            var row = _reportTable.Rows[e.RowIndex];

            var files = new List<string> { "", "" };

            var fileNames = row[ReportColumns.FullFileName.ToString()]
                .ToString()
                .Replace(Environment.NewLine, SplitChar.ToString())
                .Split(SplitChar);

            var n = 0;
            foreach (var token in fileNames)
            {
                files[n] = token;
                n++;
            }

            var paths = new List<string> { "", "" };
            var pathIds = row[ReportColumns.JsonPath.ToString()]
                .ToString()
                .Replace(Environment.NewLine, SplitChar.ToString())
                .Split(SplitChar);
            n = 0;
            foreach (var token in pathIds)
            {
                paths[n] = token;
                n++;
            }

            var fileTypes = new List<string> { "", "" };
            var fileTypeIds = row[ReportColumns.FileType.ToString()]
                .ToString()
                .Replace(Environment.NewLine, SplitChar.ToString())
                .Split(SplitChar);
            n = 0;
            foreach (var token in fileTypeIds)
            {
                fileTypes[n] = GetSaveFileNameByType(token);
                n++;
            }

            var lines = new List<int> { -1, -1 };
            var lineIds = row[ReportColumns.LineId.ToString()]
                .ToString()
                .Replace(Environment.NewLine, SplitChar.ToString())
                .Split(SplitChar);
            n = 0;
            foreach (var token in lineIds)
            {
                if (int.TryParse(token, out var line))
                    lines[n] = line;
                n++;
            }

            // if click direct to LineID - open the collected files
            if (e.ColumnIndex == (int)ReportColumns.LineId
                && !string.IsNullOrEmpty(row[ReportColumns.LineId.ToString()]
                    .ToString())
                && !string.IsNullOrEmpty(row[ReportColumns.FileType.ToString()]
                    .ToString())
                && row[ReportColumns.ValidationType.ToString()]
                    .ToString() != ValidationTypeEnum.Scheme.ToString())
            {
                for (var i = 0; i < 2; i++)
                {
                    if (string.IsNullOrEmpty(fileTypes[i]))
                    {
                        if (_editors[i] != null && !_editors[i].IsDisposed)
                        {
                            _editors[i].Close();
                        }

                        continue;
                    }

                    if (!File.Exists(fileTypes[i]))
                    {
                        continue;
                    }

                    if (_showPreview && _editors[i] != null && !_editors[i].IsDisposed)
                    {
                        _editors[i].singleLineBrackets = false;
                        _editors[i].LoadTextFromFile(fileTypes[i]);
                    }
                    else
                    {
                        _editors[i] = new JsonViewer("", "", true)
                        {
                            singleLineBrackets = false
                        };
                        _editors[i].LoadTextFromFile(fileTypes[i]);
                    }

                    _editors[i].AlwaysOnTop = _alwaysOnTop;

                    _editors[i].Show();
                    _editors[i].Text = (i == 1 ? "New value: " : "Old value: ") + fileTypes[i];

                    if (!(_editorPosition[i].WinX == 0
                        && _editorPosition[i].WinY == 0
                        && _editorPosition[i].WinW == 0
                        && _editorPosition[i].WinH == 0))
                    {
                        _editors[i].Location = new Point(_editorPosition[i].WinX, _editorPosition[i].WinY);
                        _editors[i].Width = _editorPosition[i].WinW;
                        _editors[i].Height = _editorPosition[i].WinH;
                    }

                    if (i == 0)
                    {
                        _editors[0].Closing += OnClosing1;
                    }
                    else
                    {
                        _editors[1].Closing += OnClosing2;
                    }
                    _editors[i].SelectText(lines[i] + "] ");
                }
            }
            // if click to any column except LineID - open original file
            else
            {
                for (var i = 0; i < 2; i++)
                {
                    if (string.IsNullOrEmpty(files[i]))
                    {
                        if (_editors[i] != null && !_editors[i].IsDisposed)
                        {
                            _editors[i].Close();
                        }

                        continue;
                    }

                    if (!File.Exists(files[i]))
                    {
                        continue;
                    }

                    if (_showPreview && _editors[i] != null && !_editors[i].IsDisposed)
                    {
                        _editors[i].singleLineBrackets = _reformatJson;
                        _editors[i].LoadJsonFromFile(files[i]);
                    }
                    else
                    {
                        _editors[i] = new JsonViewer(files[i], "", true)
                        {
                            singleLineBrackets = _reformatJson
                        };
                    }

                    _editors[i].AlwaysOnTop = _alwaysOnTop;
                    _editors[i].Show();
                    _editors[i].Text = (i == 1 ? "New value: " : "Old value: ") + fileNames[i];

                    if (!(_editorPosition[i].WinX == 0
                        && _editorPosition[i].WinY == 0
                        && _editorPosition[i].WinW == 0
                        && _editorPosition[i].WinH == 0))
                    {
                        _editors[i].Location = new Point(_editorPosition[i].WinX, _editorPosition[i].WinY);
                        _editors[i].Width = _editorPosition[i].WinW;
                        _editors[i].Height = _editorPosition[i].WinH;
                    }

                    if (i == 0)
                    {
                        _editors[i].Closing += OnClosing1;
                    }
                    else
                    {
                        _editors[i].Closing += OnClosing2;
                    }

                    if (TryGetPositionByPathStr(_editors[i].EditorText, paths[i], out var startPos, out var endPos))
                    {
                        _editors[i].SelectPosition(startPos, endPos + 1);
                    }

                }
            }
        }

        private void OnClosing1(object sender, CancelEventArgs e)
        {
            if (sender is Form s)
            {
                _editorPosition[0].WinX = s.Location.X;
                _editorPosition[0].WinY = s.Location.Y;
                _editorPosition[0].WinW = s.Width;
                _editorPosition[0].WinH = s.Height;
            }
        }

        private void OnClosing2(object sender, CancelEventArgs e)
        {
            if (sender is Form s)
            {
                _editorPosition[1].WinX = s.Location.X;
                _editorPosition[1].WinY = s.Location.Y;
                _editorPosition[1].WinW = s.Width;
                _editorPosition[1].WinH = s.Height;
            }
        }

        private void CheckBox_reformatJson_CheckedChanged(object sender, EventArgs e)
        {
            _reformatJson = checkBox_reformatJson.Checked;
        }

        private void RemoveThisErrorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView_report.SelectedCells.Count <= 0)
                return;

            var rowNum = dataGridView_report.SelectedCells[0].RowIndex;
            /*var currentRow = dataGridView_report.Rows[rowNum];
            dataGridView_report.Rows.Remove(currentRow);*/
            _reportTable.Rows.RemoveAt(rowNum);
        }

        private void AlwaysIgnoreThisErrorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridView_report.SelectedCells.Count <= 0)
                return;

            var rowNum = dataGridView_report.SelectedCells[0].RowIndex;
            //var currentRow = dataGridView_report.Rows[rowNum];
            var currentRow = _reportTable.Rows[rowNum];

            var newReport = new ReportItem
            {
                FullFileName = currentRow[(int)ReportColumns.FullFileName].ToString(),
                FileType = currentRow[(int)ReportColumns.FileType].ToString(),
                Message = currentRow[(int)ReportColumns.Message].ToString(),
                JsonPath = currentRow[(int)ReportColumns.JsonPath].ToString(),
                Severity = currentRow[(int)ReportColumns.Severity].ToString(),
                ValidationType = currentRow[(int)ReportColumns.ValidationType].ToString(),
                Source = currentRow[(int)ReportColumns.Source].ToString(),
            };

            if (!_ignoreReportsCollection.Any(n => n.Equals(newReport)))
            {
                _ignoreReportsCollection.Add(newReport);
                //dataGridView_report.Rows.Remove(currentRow);
                _reportTable.Rows.RemoveAt(rowNum);

                var ignoreFile = _projectName + "\\" + IgnoreFileName;
                if (!JsonIo.SaveJson(_ignoreReportsCollection, ignoreFile, true))
                {
                    _textLog.AppendLine("Can't save file: " + ignoreFile);
                }
            }
        }

        private void DataGridView_report_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;

            if (sender is DataGridView dataGrid)
            {
                dataGrid.ClearSelection();
                if (e.RowIndex < 0 || e.ColumnIndex < 0)
                {
                    contextMenuStrip1.Enabled = false;
                    return;
                }

                contextMenuStrip1.Enabled = true;
                dataGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Selected = true;
            }
        }

        private void CheckedListBox_validators_Leave(object sender, EventArgs e)
        {
            _checkedValidators.Clear();
            foreach (var validator in checkedListBox_validators.CheckedItems)
            {
                if (_validatorsList.TryGetValue(validator.ToString(), out var v))
                {
                    _checkedValidators.Add(v.Method.Name);
                }
            }
        }

        private void DataGridView_report_SelectionChanged(object sender, EventArgs e)
        {
            if (dataGridView_report.SelectedCells.Count <= 0)
                return;

            var newColumn = dataGridView_report.SelectedCells[0].ColumnIndex;
            var newRow = dataGridView_report.SelectedCells[0].RowIndex;

            if (_oldColumn == (int)ReportColumns.LineId && newColumn != (int)ReportColumns.LineId
                || _oldColumn != (int)ReportColumns.LineId && newColumn == (int)ReportColumns.LineId
                || _oldRow != newRow)
            {
                var param = new DataGridViewCellEventArgs(newColumn,
                    newRow);
                DataGridView_report_CellDoubleClick(dataGridView_report, param);
                dataGridView_report.Focus();
                _oldRow = newRow;
                _oldColumn = newColumn;
            }
        }

        private void CheckBox_showPreview_CheckedChanged(object sender, EventArgs e)
        {
            _showPreview = checkBox_showPreview.Checked;

            if (_showPreview)
            {
                this.dataGridView_report.SelectionChanged += new EventHandler(this.DataGridView_report_SelectionChanged);
            }
            else
            {
                this.dataGridView_report.SelectionChanged -= new EventHandler(this.DataGridView_report_SelectionChanged);
            }
        }

        private void CheckBox_saveFiles_CheckedChanged(object sender, EventArgs e)
        {
            _saveTmpFiles = checkBox_saveFiles.Checked;
        }

        private void CheckBox_saveReport_CheckedChanged(object sender, EventArgs e)
        {
            _saveReport = checkBox_saveReport.Checked;
        }

        private void Button_selectAssemblyFolder_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.SelectedPath = _serverAssembliesPath;
            if (folderBrowserDialog1.ShowDialog() == DialogResult.OK &&
                !string.IsNullOrEmpty(folderBrowserDialog1.SelectedPath))
            {
                _serverAssembliesPath = folderBrowserDialog1.SelectedPath;
            }
        }

        #endregion

        #region Helpers

        private void RunCommandLine(string[] args)
        {
            _runFromCmdLine = true;
            _saveTmpFiles = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "-i" || args[i] == "/i")
                {
                    _ignoreHttpsError = true;
                }
                else if (args[i] == "-s" || args[i] == "/s")
                {
                    _skipSchemaErrors = true;
                }
                else if (args[i] == "-t" || args[i] == "/t")
                {
                    _saveTmpFiles = true;
                }
                else if (args[i] == "-h" || args[i] == "/h" || args[i] == "-?" || args[i] == "/?")
                {
                    Console.WriteLine(HelpString);
                    Exit();
                }
                else if (string.IsNullOrEmpty(_projectPath))
                {
                    SetProject(args[i], false);
                }
            }

            if (string.IsNullOrEmpty(_projectPath))
            {
                Console.WriteLine("No root folder specified.");
                Console.WriteLine(HelpString);
                Exit();
            }

            if (!Directory.Exists(_projectPath))
            {
                Console.WriteLine("Project folder does not exists.");
                Exit();
            }

            if (_isCollectionFolder)
            {
                Button_validateAll_Click(this, EventArgs.Empty);
            }
            else
            {
                Button_validateProject_Click(this, EventArgs.Empty);
            }

            Exit();
        }

        private List<ReportItem> RunValidation(bool fullInit, bool saveFile = true)
        {
            if (Directory.Exists(_projectPath + "\\..\\shared\\")
                && _projectPath.Contains("\\Deployment\\Server\\Apps\\"))
            {
                _folderType = FolderType.Deployment;
                //_textLog.AppendLine("Project is in the \"\\Deployment\\Server\\Apps\\MetaUI\\\" folder");
            }
            else if (Directory.Exists(_projectPath + "\\..\\..\\shared\\"))
            {
                _folderType = FolderType.Repository;
                //_textLog.AppendLine("Project is in the \"\\MetaUI\\\" folder");
            }
            else if (Directory.Exists(_projectPath + "\\..\\..\\..\\shared\\")
                && Utilities.GetShortFileName(_projectPath).StartsWith("Ice"))
            {
                _folderType = FolderType.IceRepository;
                //_textLog.AppendLine("Project is in the \"\\MetaUI\\ICE\\\" folder");
            }
            else
            {
                _folderType = FolderType.Unknown;
            }

            SetStatus("Searching project files...");
            // collect default project file list
            foreach (var file in _initialProjectFiles)
            {
                var fullFileName = _projectPath + "\\" + file;
                if (!File.Exists(fullFileName)
                    && file != "strings.jsonc"
                    && !Utilities.IsShared(fullFileName, _projectPath))
                {
                    var report = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = fullFileName,
                        Message = "Mandatory project file not found",
                        ValidationType = ValidationTypeEnum.File.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "RunValidation"
                    };
                    _RunValidationReportsCollection.Add(report);
                }
                else
                {
                    _filesList.Add(fullFileName);
                }
            }

            try
            {
                _filesList.AddRange(Directory.GetFiles(_projectPath + "\\pages",
                    FileMask,
                    SearchOption.TopDirectoryOnly));
            }
            catch
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    Message = "Page folder files not found.",
                    ValidationType = ValidationTypeEnum.File.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = "RunValidation"
                };
                _RunValidationReportsCollection.Add(report);
            }

            FlushLog();

            // parse all project files and include imports
            for (var i = 0; i < _filesList.Count; i++)
            {
                SetStatus($"Parsing {_filesList[i]} [{i}/{_filesList.Count}]");
                var fileType = Utilities.GetFileTypeFromFileName(_filesList[i], _fileTypes);
                DeserializeFile(_filesList[i], fileType, _jsonPropertiesCollection, _processedFilesList, 0);
            }

            FlushLog();

            // enumerate properties (LineId)
            for (var maxId = 0; maxId < _jsonPropertiesCollection.Count; maxId++)
                _jsonPropertiesCollection[maxId].LineId = maxId;

            //save complete collections by fileTypes
            var i1 = 1;
            if (saveFile)
            {
                foreach (var item in _fileTypes)
                {
                    var fileName = GetSaveFileNameByType(item.FileType.ToString());

                    SetStatus($"Saving total file: {fileName} [{i1}/{_fileTypes.Count}]");
                    var singleTypeCollection = _jsonPropertiesCollection
                        .Where(n => n.FileType == item.FileType);
                    var typeCollection = singleTypeCollection as JsonProperty[] ?? singleTypeCollection.ToArray();
                    SaveCollectionToFile(typeCollection, fileName);
                    i1++;
                }
            }

            FlushLog();

            // initialize validator settings
            InitValidator(fullInit);

            // run every validator selected
            var reportsCount = 0;
            var reportsCollection = new List<ReportItem>();
            foreach (var validator in _validatorsList)
            {
                var validatorMethod = validator.Value.Method.Name;
                if (!_checkedValidators.Contains(validatorMethod))
                    continue;

                SetStatus($"Validating: {validator.Key}");

                var report = validator.Value(validatorMethod);
                reportsCollection.AddRange(report);
                FlushLog();
            }

            _textLog.AppendLine($"{_projectName} files validated: {_processedFilesList.Count}, issues found: {reportsCollection.Count}");
            return reportsCollection;
        }

        private void ProcessReport(List<ReportItem> reports, bool saveFile = true, bool updateTable = true)
        {
            // get list of globally ignored errors
            var systemIgnoreReportsCollection = JsonIo.LoadJson<ReportItem>(GlobalIgnoreFileName);

            // get list of ignored errors and filter them out from report
            var ignoreFile = _projectName + "\\" + IgnoreFileName;
            this._ignoreReportsCollection.AddRange(JsonIo.LoadJson<ReportItem>(ignoreFile));
            if (_ignoreReportsCollection.Any() || systemIgnoreReportsCollection.Any())
            {
                for (var i = 0; i < reports.Count; i++)
                {
                    if (systemIgnoreReportsCollection.Any(n => n.Equals(reports[i])))
                    {
                        reports.RemoveAt(i);
                        i--;
                    }
                    if (_ignoreReportsCollection.Any(n => n.Equals(reports[i])))
                    {
                        reports.RemoveAt(i);
                        i--;
                    }
                }
            }

            // transfer report to table
            if (!_runFromCmdLine && updateTable)
            {
                foreach (var reportLine in reports)
                {
                    var newRow = _reportTable.NewRow();
                    newRow[ReportColumns.ProjectName.ToString()] = reportLine.ProjectName;
                    newRow[ReportColumns.LineId.ToString()] = reportLine.LineId.Replace(SplitChar.ToString(), Environment.NewLine);
                    newRow[ReportColumns.FileType.ToString()] = reportLine.FileType.Replace(SplitChar.ToString(), Environment.NewLine);
                    newRow[ReportColumns.Message.ToString()] = reportLine.Message;
                    newRow[ReportColumns.JsonPath.ToString()] = reportLine.JsonPath.Replace(SplitChar.ToString(), Environment.NewLine);
                    newRow[ReportColumns.ValidationType.ToString()] = reportLine.ValidationType;
                    newRow[ReportColumns.Severity.ToString()] = reportLine.Severity;
                    newRow[ReportColumns.FullFileName.ToString()] =
                        reportLine.FullFileName.Replace(SplitChar.ToString(), Environment.NewLine);
                    _reportTable.Rows.Add(newRow);
                }
            }

            if (saveFile)
            {
                try
                {
                    if (!Directory.Exists(_projectName))
                        Directory.CreateDirectory(_projectName);
                }
                catch (Exception ex)
                {
                    _textLog.AppendLine($"Can't find directory: {_projectName}{Environment.NewLine}{ex}");
                    return;
                }

                // save report to json file
                var reportFileName = _projectName + "\\" + ReportJsonFileName;
                if (!JsonIo.SaveJson(reports, reportFileName, true))
                {
                    _textLog.AppendLine("Can't save file: " + reportFileName);
                }
            }
        }

        private void DeserializeFile(
            string fullFileName,
            JsoncContentType fileType,
            List<JsonProperty> rootCollection,
            Dictionary<string, string> processedList,
            int jsonDepth)
        {
            if (processedList.ContainsKey(fullFileName))
                return;

            processedList.Add(fullFileName, "");
            string jsonStr;
            try
            {
                jsonStr = File.ReadAllText(fullFileName);
            }
            catch (Exception ex)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = fullFileName,
                    Message = "File read exception: " + Utilities.ExceptionPrint(ex),
                    ValidationType = ValidationTypeEnum.File.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "DeserializeFile"
                };
                _ParseJsonObjectReportsCollection.Add(report);
                return;
            }

            if (string.IsNullOrEmpty(jsonStr))
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = fullFileName,
                    Message = "File is empty",
                    ValidationType = ValidationTypeEnum.File.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = "DeserializeFile"
                };
                _ParseJsonObjectReportsCollection.Add(report);
                return;
            }

            dynamic jsonObject;
            try
            {
                var jsonSettings = new JsonSerializerSettings
                {
                    Formatting = Formatting.None
                };

                jsonObject = JsonConvert.DeserializeObject(jsonStr, jsonSettings);
            }
            catch (Exception ex)
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = fullFileName,
                    Message = "File parse exception: " + Utilities.ExceptionPrint(ex),
                    ValidationType = ValidationTypeEnum.Parse.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "DeserializeFile"
                };
                _ParseJsonObjectReportsCollection.Add(report);
                return;
            }

            var ver = "";
            //var jsonDepth = 0;
            var shared = Utilities.IsShared(fullFileName, _projectPath);
            if (jsonObject != null && jsonObject is JToken)
                ParseJsonObject(
                    jsonObject,
                    fileType,
                    rootCollection,
                    fullFileName,
                    processedList,
                    ref ver,
                    jsonDepth,
                    "",
                    shared);
        }

        private void ParseJsonObject(
            JToken token,
            JsoncContentType fileType,
            List<JsonProperty> rootCollection,
            string fullFileName,
            Dictionary<string, string> processedList,
            ref string version,
            int jsonDepth,
            string parent,
            bool shared)
        {
            if (token == null)
                return;
            if (rootCollection == null)
                return;

            switch (token)
            {
                case JProperty jProperty:
                {
                    var jsonPath = jProperty.Path;
                    var propValue = "";
                    var jValue = jProperty.Value;
                    var name = jProperty.Name;
                    if (jValue is JValue jPropertyValue)
                        propValue = jPropertyValue.Value?.ToString();

                    // get schema version
                    if (name == VersionTagName)
                    {
                        if (!string.IsNullOrEmpty(version))
                        {
                            var report = new ReportItem
                            {
                                ProjectName = _projectName,
                                FullFileName = fullFileName,
                                JsonPath = jsonPath,
                                Message = $"Scheme version inconsistent: {version}->{propValue}",
                                ValidationType = ValidationTypeEnum.Logic.ToString(),
                                Severity = ImportanceEnum.Error.ToString(),
                                Source = "ParseJsonObject"
                            };
                            _ParseJsonObjectReportsCollection.Add(report);
                        }

                        version = propValue;
                    }
                    else if (name == SchemaTagName)
                    {
                        processedList[fullFileName] = propValue;
                    }

                    var newProperty = new JsonProperty
                    {
                        Value = propValue ?? "",
                        FileType = fileType,
                        FullFileName = fullFileName,
                        JsonPath = jsonPath,
                        JsonDepth = jsonDepth,
                        Name = name,
                        Version = version,
                        ItemType = JsonItemType.Property,
                        Parent = parent,
                        Shared = shared
                    };
                    rootCollection.Add(newProperty);

                    // try to import file
                    if (name == FileTagName)
                    {
                        var importFileName = "";
                        if (_folderType == FolderType.Unknown) //deployment folder
                        {
                            var report = new ReportItem
                            {
                                ProjectName = _projectName,
                                FullFileName = propValue,
                                FileType = "",
                                JsonPath = jsonPath,
                                Message = "Folder type not recognized (Deployment/Repository/...)",
                                ValidationType = ValidationTypeEnum.File.ToString(),
                                Severity = ImportanceEnum.Warning.ToString(),
                                Source = "ParseJsonObject"
                            };
                            _ParseJsonObjectReportsCollection.Add(report);
                            importFileName = GetFileFromRef(propValue);
                        }
                        else if (_folderType == FolderType.Deployment) //deployment folder
                        {
                            importFileName = GetFileFromRef(propValue);
                        }
                        else // MetaUI folder
                        {
                            if (_folderType == FolderType.IceRepository)
                            {
                                // still in project folder
                                if (fullFileName.Contains(_projectPath))
                                {
                                    importFileName = GetFileFromRef(propValue);
                                    // goes to shared
                                    if (importFileName.StartsWith(".."))
                                        importFileName = "..\\..\\" + importFileName;
                                }
                                // already in shared
                                else
                                {
                                    importFileName = GetFileFromRef(propValue);
                                }
                            }
                            else if (_folderType == FolderType.Repository)
                            {
                                // still in project folder
                                if (fullFileName.Contains(_projectPath))
                                {
                                    importFileName = GetFileFromRef(propValue);
                                    // goes to shared
                                    if (importFileName.StartsWith(".."))
                                        importFileName = "..\\" + importFileName;
                                }
                                // already in shared
                                else
                                {
                                    importFileName = GetFileFromRef(propValue);
                                }
                            }
                        }

                        importFileName = GetFilePath(fullFileName)
                            + importFileName.Replace('/', '\\');
                        importFileName = SimplifyPath(importFileName);

                        var importFileType = GetFileTypeFromJsonPath(jsonPath);
                        if (importFileType == JsoncContentType.Unknown)
                        {
                            importFileType = Utilities.GetFileTypeFromFileName(importFileName, _fileTypes);
                        }

                        if (!File.Exists(importFileName))
                        {
                            var report = new ReportItem
                            {
                                ProjectName = _projectName,
                                FullFileName = importFileName,
                                FileType = importFileType.ToString(),
                                JsonPath = jsonPath,
                                Message = "File doesn't exists",
                                ValidationType = ValidationTypeEnum.File.ToString(),
                                Severity = ImportanceEnum.Error.ToString(),
                                Source = "ParseJsonObject"
                            };
                            _ParseJsonObjectReportsCollection.Add(report);
                        }
                        else
                        {
                            jsonDepth++;
                            DeserializeFile(
                                importFileName,
                                importFileType,
                                _jsonPropertiesCollection,
                                processedList,
                                jsonDepth);
                            jsonDepth--;
                        }
                    }

                    // get new file type
                    if (_fileTypes.Any(n => n.PropertyTypeName == name) && jsonPath.StartsWith(ImportTagName))
                    {
                        var newFileType = _fileTypes
                            .FirstOrDefault(n => n.PropertyTypeName == name).FileType;
                        if (fileType != newFileType)
                        {
                            if (fileType != JsoncContentType.Unknown && newFileType != JsoncContentType.Patch)
                            {
                                var report = new ReportItem
                                {
                                    ProjectName = _projectName,
                                    FullFileName = fullFileName,
                                    JsonPath = jsonPath,
                                    Message = $"File type inconsistent: {fileType}->{newFileType}",
                                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                                    Severity = newFileType == JsoncContentType.Patch
                                        ? ImportanceEnum.Note.ToString()
                                        : ImportanceEnum.Warning.ToString(),
                                    Source = "ParseJsonObject"
                                };
                                _ParseJsonObjectReportsCollection.Add(report);
                            }

                            newProperty.FileType = fileType = newFileType;
                        }
                    }

                    foreach (var child in jProperty.Children())
                        if (child is JArray || child is JObject)
                        {
                            jsonDepth++;
                            var newParent = string.IsNullOrEmpty(name) ? parent : name;
                            ParseJsonObject(
                                child,
                                fileType,
                                rootCollection,
                                fullFileName,
                                processedList,
                                ref version,
                                jsonDepth,
                                newParent,
                                shared);
                            jsonDepth--;
                        }

                    break;
                }
                case JObject jObject:
                {
                    if (jObject == null)
                    {
                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = fullFileName,
                            JsonPath = token.Path,
                            Message = "Null object skipped by parser: " + token,
                            ValidationType = ValidationTypeEnum.Parse.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "ParseJsonObject"
                        };
                        _ParseJsonObjectReportsCollection.Add(report);

                        break;
                    }
                    var newProperty = new JsonProperty
                    {
                        FileType = fileType,
                        FullFileName = fullFileName,
                        JsonDepth = jsonDepth,
                        Name = "{",
                        Version = version,
                        ItemType = JsonItemType.Object,
                        Parent = parent,
                        Shared = shared
                    };
                    rootCollection.Add(newProperty);

                    foreach (var child in jObject.Children())
                        ParseJsonObject(
                            child,
                            fileType,
                            rootCollection,
                            fullFileName,
                            processedList,
                            ref version,
                            jsonDepth,
                            parent,
                            shared);

                    newProperty = new JsonProperty
                    {
                        FileType = fileType,
                        FullFileName = fullFileName,
                        JsonDepth = jsonDepth,
                        Name = "}",
                        Version = version,
                        ItemType = JsonItemType.Object,
                        Parent = parent,
                        Shared = shared
                    };
                    rootCollection.Add(newProperty);
                    break;
                }
                case JArray jArray:
                {
                    if (jArray == null)
                    {
                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = fullFileName,
                            JsonPath = token.Path,
                            Message = "Null array skipped by parser: " + token,
                            ValidationType = ValidationTypeEnum.Parse.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "ParseJsonObject"
                        };
                        _ParseJsonObjectReportsCollection.Add(report);

                        break;
                    }
                    var newProperty = new JsonProperty
                    {
                        FileType = fileType,
                        FullFileName = fullFileName,
                        JsonDepth = jsonDepth - 1,
                        Name = "[",
                        Version = version,
                        ItemType = JsonItemType.Array,
                        Parent = parent,
                        Shared = shared
                    };
                    rootCollection.Add(newProperty);

                    foreach (var child in jArray.Children())
                        ParseJsonObject(
                            child,
                            fileType,
                            rootCollection,
                            fullFileName,
                            processedList,
                            ref version,
                            jsonDepth,
                            parent,
                            shared);

                    newProperty = new JsonProperty
                    {
                        FileType = fileType,
                        FullFileName = fullFileName,
                        JsonDepth = jsonDepth - 1,
                        Name = "]",
                        Version = version,
                        ItemType = JsonItemType.Array,
                        Parent = parent,
                        Shared = shared
                    };
                    rootCollection.Add(newProperty);
                    break;
                }
                default:
                {
                    if (token.Children().Any())
                    {
                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = fullFileName,
                            JsonPath = token.Path,
                            Message = "Unknown node skipped by parser: " + token,
                            ValidationType = ValidationTypeEnum.Parse.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "ParseJsonObject"
                        };
                        _ParseJsonObjectReportsCollection.Add(report);
                    }
                }
                break;
            }
        }

        private void ActivateUiControls(bool active)
        {
            if (_runFromCmdLine)
                return;

            if (active)
            {
                dataGridView_report.DataSource = _reportTable;
                dataGridView_report.Invalidate();
            }
            else
            {
                dataGridView_report.DataSource = null;
            }

            button_validateProject.Enabled = active
                && !string.IsNullOrEmpty(_projectPath)
                && !_isCollectionFolder;
            button_validateAll.Enabled = active
                && !string.IsNullOrEmpty(_projectPath)
                && _isCollectionFolder;
            button_SelectProject.Enabled = active;
            tabControl1.Enabled = active;

            Refresh();
        }

        private void SetProject(string path, bool loadReport = true)
        {
            _projectPath = path.TrimEnd('\\');
            _projectName = Utilities.GetShortFileName(_projectPath);

            // check if it's a collection or project folder
            if (string.IsNullOrEmpty(_projectPath))
                return;

            _isCollectionFolder = false;
            var n = 0;
            foreach (var file in _initialProjectFiles)
            {
                var fullFileName = _projectPath + "\\" + file;
                if (!File.Exists(fullFileName))
                {
                    n++;
                }
            }

            if (n > _initialProjectFiles.Length / 2)
            {
                _isCollectionFolder = true;
            }


            if (!_runFromCmdLine)
            {
                folderBrowserDialog1.SelectedPath = path;
                MainFormCaption = DefaultFormCaption + ": " + _projectName;

                //load report if already exists
                if (loadReport)
                {
                    InitReportsGrid(_isCollectionFolder);
                    var reportFileName = _projectName + "\\" + ReportJsonFileName;
                    if (File.Exists(reportFileName))
                    {
                        var reportsCollection = JsonIo.LoadJson<ReportItem>(reportFileName);
                        foreach (var reportLine in reportsCollection)
                        {
                            var newRow = _reportTable.NewRow();
                            newRow[ReportColumns.ProjectName.ToString()] = reportLine.ProjectName;
                            newRow[ReportColumns.LineId.ToString()] = reportLine.LineId.Replace(SplitChar.ToString(), Environment.NewLine);
                            newRow[ReportColumns.FileType.ToString()] = reportLine.FileType.Replace(SplitChar.ToString(), Environment.NewLine);
                            newRow[ReportColumns.Message.ToString()] = reportLine.Message;
                            newRow[ReportColumns.JsonPath.ToString()] = reportLine.JsonPath.Replace(SplitChar.ToString(), Environment.NewLine);
                            newRow[ReportColumns.ValidationType.ToString()] = reportLine.ValidationType;
                            newRow[ReportColumns.Severity.ToString()] = reportLine.Severity;
                            newRow[ReportColumns.FullFileName.ToString()] =
                                reportLine.FullFileName.Replace(SplitChar.ToString(), Environment.NewLine);
                            _reportTable.Rows.Add(newRow);
                        }
                    }

                    var ignoreFile = _projectName + "\\" + IgnoreFileName;
                    if (File.Exists(ignoreFile))
                    {
                        _ignoreReportsCollection = JsonIo.LoadJson<ReportItem>(ignoreFile);
                    }
                }
            }
        }

        private void InitReportsGrid(bool collection = false)
        {
            if (_runFromCmdLine)
                return;

            _reportTable = new DataTable("Examples");
            _reportTable.Clear();
            _reportTable.Rows.Clear();
            _reportTable.Columns.Clear();
            {
                this.dataGridView_report.SelectionChanged -= new EventHandler(this.DataGridView_report_SelectionChanged);
                dataGridView_report.ClearSelection();
                dataGridView_report.Columns.Clear();
            }

            foreach (var col in Enum.GetNames(typeof(ReportColumns)))
            {
                _reportTable.Columns.Add(col);

                var column = new DataGridViewTextBoxColumn
                {
                    DataPropertyName = col,
                    Name = col,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
                    SortMode = DataGridViewColumnSortMode.NotSortable // temp. disabled
                };
                if (col == ReportColumns.JsonPath.ToString()
                    || col == ReportColumns.Message.ToString())
                {
                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    column.Width = 50;
                }

                if (col == ReportColumns.ProjectName.ToString() && !collection)
                {
                    column.Visible = false;
                }

                dataGridView_report.Columns.Add(column);

            }

            dataGridView_report.DataError += delegate
            { };
            dataGridView_report.RowHeadersVisible = false;
            dataGridView_report.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
            dataGridView_report.Columns[ReportColumns.Message.ToString()].Width =
                Settings.Default.MessageColumnWidth;
            // reserved for future use
            dataGridView_report.Columns[ReportColumns.Source.ToString()].Visible = false;
            dataGridView_report.Columns[ReportColumns.ValidationType.ToString()].Visible = false;

            if (_showPreview)
            {
                this.dataGridView_report.SelectionChanged += new EventHandler(this.DataGridView_report_SelectionChanged);
            }

        }

        private void SaveCollectionToFile(JsonProperty[] typeCollection, string fileName)
        {
            if (typeCollection.Any())
            {
                try
                {
                    if (!Directory.Exists(_projectName))
                        Directory.CreateDirectory(_projectName);
                }
                catch (Exception ex)
                {
                    _textLog.AppendLine($"Can't find directory: {_projectName}{Environment.NewLine}{ex}");
                    return;
                }

                JsonIo.SaveJson(typeCollection, fileName + ".json", true);

                var maxId = _jsonPropertiesCollection[_jsonPropertiesCollection.Count - 1].LineId;
                var maxIdLength = maxId.ToString().Length;
                var fileContent = new StringBuilder();
                var jsonIdent = 4;

                foreach (var property in typeCollection)
                {
                    var str = "["
                        + property.LineId.ToString("D" + maxIdLength)
                        + "] "
                        + new string(' ', property.JsonDepth * jsonIdent)
                        + property.Name;
                    if (property.ItemType == JsonItemType.Array
                        || property.ItemType == JsonItemType.Object)
                    {
                        fileContent.AppendLine(str);
                    }
                    else
                    {
                        fileContent.AppendLine(str + ": \"" + property.Value + "\"");
                    }
                }

                try
                {
                    File.WriteAllText(fileName, fileContent.ToString());
                }
                catch (Exception ex)
                {
                    _textLog.AppendLine($"Can't save file: {fileName}{Environment.NewLine}{ex}");
                }
            }
        }

        private void InitValidator(bool fullInit)
        {
            // initialize validator settings
            ProcessConfiguration procConf = null;
            if (fullInit)
            {
                procConf = new ProcessConfiguration()
                {
                    BackupSchemaExtension = BackupSchemaExtension,
                    FileMask = FileMask,
                    FileTypes = _fileTypes,
                    IgnoreHttpsError = _ignoreHttpsError,
                    SchemaTag = SchemaTag,
                    SkipSchemaErrors = _skipSchemaErrors,
                    SplitChar = SplitChar,
                    SuppressSchemaErrors = _suppressSchemaErrors,
                    SystemDataViews = _systemDataViews,
                    SystemMacros = _systemMacros,
                    ServerAssembliesPath = _serverAssembliesPath
                };
            }

            var projConf = new ProjectConfiguration
            {
                FolderType = _folderType,
                ProjectName = _projectName,
                ProjectPath = _projectPath
            };

            var seedData = new SeedData
            {
                JsonPropertiesCollection = _jsonPropertiesCollection,
                DeserializeFileReportsCollection = _DeserializeFileReportsCollection,
                ParseJsonObjectReportsCollection = _ParseJsonObjectReportsCollection,
                ProcessedFilesList = _processedFilesList,
                RunValidationReportsCollection = _RunValidationReportsCollection
            };

            ProjectValidator.Initialize(procConf, projConf, seedData, fullInit);
        }
        #endregion

        #region Utilities

        private void FlushLog()
        {
            if (_textLog.Length > 0)
            {
                if (!_runFromCmdLine)
                {
                    Invoke((MethodInvoker)delegate
                   {
                       textBox_logText.Text += _textLog.ToString();
                       textBox_logText.SelectionStart = textBox_logText.Text.Length;
                       textBox_logText.ScrollToCaret();
                   });
                }
                else
                {
                    Console.WriteLine(_textLog.ToString());
                }

                _textLog.Clear();
            }
        }

        private void SetStatus(string status)
        {
            if (!_runFromCmdLine)
            {
                Invoke((MethodInvoker)delegate
               {
                   toolStripStatusLabel1.Text = status;
               });
            }
            else
            {
                if (!string.IsNullOrEmpty(status))
                    Console.WriteLine(status);
            }
        }

        private string GetFilePath(string longFileName)
        {
            var i = longFileName.LastIndexOf('\\');
            return longFileName.Substring(0, i + 1);
        }

        private JsoncContentType GetFileTypeFromJsonPath(string jsonPath)
        {
            var fileType = JsoncContentType.Unknown;
            var typeName = "";

            if (jsonPath.StartsWith(ImportTagName) && jsonPath.EndsWith(FileTagName))
            {
                typeName = jsonPath.Replace("." + FileTagName, "");
                typeName = typeName.Substring(typeName.LastIndexOf('.') + 1);
            }

            foreach (var item in _fileTypes)
            {
                if (typeName == item.PropertyTypeName)
                {
                    fileType = item.FileType;
                    break;
                }
            }

            return fileType;
        }

        private string GetSaveFileNameByType(string fileType, bool noExtension = false)
        {
            var fileName = "";
            foreach (var file in _fileTypes)
                if (file.FileType.ToString() == fileType)
                {
                    var projectName = Utilities.GetShortFileName(_projectPath);
                    if (noExtension)
                        fileName = projectName
                            + "\\_full-"
                            + file.FileTypeMask.Substring(0, file.FileTypeMask.LastIndexOf('.'));
                    else
                        fileName = projectName
                            + "\\_full-"
                            + file.FileTypeMask.Replace(".jsonc", ".txt");
                    break;
                }

            return fileName;
        }

        private string GetFileFromRef(string refString)
        {
            var pos = refString.IndexOf('#');
            return pos > 0 ? refString.Substring(0, pos) : "";
        }

        private string SimplifyPath(string path)
        {
            if (!path.Contains(".\\"))
                return path;

            var chunks = new List<string>();
            chunks.AddRange(path.Split('\\'));
            for (var i = 1; i < chunks.Count; i++)
                if (chunks[i] == ".." && i > 0 && chunks[i - 1] != "..")
                {
                    chunks.RemoveAt(i - 1);
                    i--;
                    chunks.RemoveAt(i);
                    i--;
                }
                else if (chunks[i] == "." && i > 0)
                {
                    chunks.RemoveAt(i);
                    i--;
                }

            var simplePath = new StringBuilder();
            foreach (var chunk in chunks)
                if (simplePath.Length > 0)
                    simplePath.Append("\\" + chunk);
                else
                    simplePath.Append(chunk);
            return simplePath.ToString();
        }

        private void Exit()
        {
            if (Application.MessageLoop)
                // WinForms app
                Application.Exit();
            else
                // Console app
                Environment.Exit(1);
        }

        private bool TryGetPositionByPathStr(string json, string path, out int startPos, out int endPos)
        {
            startPos = -1;
            endPos = -1;

            var pathList = ParseJsonPathsStr(json.Replace(' ', ' '), true);

            var pathItems = pathList.Where(n => n.Path == path).ToArray();
            if (pathItems.Any())
            {
                startPos = pathItems.Last().StartPosition;
                endPos = pathItems.Last().EndPosition;
                return true;
            }

            return false;
        }

        #endregion

    }
}
