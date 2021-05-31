// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++, C#, and Java: http://www.viva64.com

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
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

        private const string DefaultFormCaption = "KineticValidator";
        private const string IgnoreFileName = "ignore.json";
        private const string GlobalIgnoreFileName = "ignore.json";
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
                FileType = JsoncContentType.Events
            },
            new ContentTypeItem
            {
                FileTypeMask = "layout.jsonc",
                PropertyTypeName = "layout",
                FileType = JsoncContentType.Layout
            },
            new ContentTypeItem
            {
                FileTypeMask = "rules.jsonc",
                PropertyTypeName = "rules",
                FileType = JsoncContentType.Rules
            },
            new ContentTypeItem
            {
                FileTypeMask = "search.jsonc",
                PropertyTypeName = "search",
                FileType = JsoncContentType.Search
            },
            new ContentTypeItem
            {
                FileTypeMask = "combo.jsonc",
                PropertyTypeName = "combo",
                FileType = JsoncContentType.Combo
            },
            new ContentTypeItem
            {
                FileTypeMask = "tools.jsonc",
                PropertyTypeName = "tools",
                FileType = JsoncContentType.Tools
            },
            new ContentTypeItem
            {
                FileTypeMask = "strings.jsonc",
                PropertyTypeName = "strings",
                FileType = JsoncContentType.Strings
            },
            new ContentTypeItem
            {
                FileTypeMask = "patch.jsonc",
                PropertyTypeName = "patch",
                FileType = JsoncContentType.Patch
            }
        };

        // behavior options
        private bool _ignoreHttpsError;
        private bool _alwaysOnTop;
        private bool _reformatJson;
        private bool _skipSchemaErrors;
        private bool _patchAllFields;
        private bool _runFromCmdLine;
        private bool _saveTmpFiles;
        private bool _saveReport;
        private bool _isCollectionFolder;
        private bool _showPreview;
        private bool _useVsCode;
        private FolderType _folderType;

        private struct WinPosition
        {
            public int WinX;
            public int WinY;
            public int WinW;
            public int WinH;
        }

        private readonly WinPosition[] _editorPosition = new WinPosition[2];

        // global variables
        private readonly StringBuilder _textLog = new StringBuilder();
        private string _projectPath = "";
        private string _projectName = "";
        private int _oldColumn = -1;
        private int _oldRow = -1;

        //schema URL, schema text
        private BlockingCollection<JsonProperty> _jsonPropertiesCollection = new BlockingCollection<JsonProperty>();

        // full file name, schema URL
        private DataTable _reportTable = new DataTable();

        private readonly Dictionary<string, Func<string, IEnumerable<ReportItem>>> _validatorsList;

        private readonly List<string> _checkedValidators;

        private readonly JsonViewer[] _editors = new JsonViewer[2];

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
                if (Enum.TryParse(error, out ValidationErrorKind errKind))
                    _suppressSchemaErrors.Add(errKind);

            _validatorsList = ProjectValidator.ValidatorsList;

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
            checkBox_vsCode.Checked = _useVsCode = Settings.Default.UseVsCode;

            foreach (var validator in _validatorsList)
            {
                var checkedValidator = _checkedValidators.Contains(validator.Value.Method.Name);
                checkedListBox_validators.Items.Add(validator.Key, checkedValidator);
            }

            var winX = Settings.Default.MainWindowPositionX;
            var winY = Settings.Default.MainWindowPositionY;
            var winW = Settings.Default.MainWindowWidth;
            var winH = Settings.Default.MainWindowHeight;

            if (!(winX == 0 && winY == 0 && winW == 0 && winH == 0))
            {
                Location = new Point
                { X = winX, Y = winY };
                Width = winW;
                Height = winH;
            }

            SetProject(Settings.Default.LastProjectFolder);
            ActivateUiControls(true);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            dataGridView_report.SelectionChanged -= DataGridView_report_SelectionChanged;

            Settings.Default.ServerAssembliesPath = _serverAssembliesPath;
            Settings.Default.LastProjectFolder = _projectPath;
            Settings.Default.IgnoreHttpsError = _ignoreHttpsError;
            Settings.Default.AlwaysOnTop = _alwaysOnTop;
            Settings.Default.SkipSchemaErrors = _skipSchemaErrors;
            Settings.Default.ReformatJson = _reformatJson;
            Settings.Default.ShowPreview = _showPreview;
            Settings.Default.UseVsCode = _useVsCode;
            Settings.Default.SaveTmpFiles = _saveTmpFiles;
            Settings.Default.SaveReport = _saveReport;
            Settings.Default.MainWindowPositionX = Location.X;
            Settings.Default.MainWindowPositionY = Location.Y;
            Settings.Default.MainWindowWidth = Width;
            Settings.Default.MainWindowHeight = Height;

            for (var i = 0; i < 2; i++)
                if (_editors[i] != null && !_editors[i].IsDisposed)
                {
                    _editorPosition[i].WinX = _editors[i].Location.X;
                    _editorPosition[i].WinY = _editors[i].Location.Y;
                    _editorPosition[i].WinW = _editors[i].Width;
                    _editorPosition[i].WinH = _editors[i].Height;
                }

            Settings.Default.Editor1PositionX = _editorPosition[0].WinX;
            Settings.Default.Editor1PositionY = _editorPosition[0].WinY;
            Settings.Default.Editor1Width = _editorPosition[0].WinW;
            Settings.Default.Editor1Height = _editorPosition[0].WinH;
            Settings.Default.Editor2PositionX = _editorPosition[1].WinX;
            Settings.Default.Editor2PositionY = _editorPosition[1].WinY;
            Settings.Default.Editor2Width = _editorPosition[1].WinW;
            Settings.Default.Editor2Height = _editorPosition[1].WinH;

            Settings.Default.MessageColumnWidth =
                dataGridView_report.Columns[ReportColumns.Message.ToString()]?.Width ?? 100;

            var enabledValidators = new StringBuilder();
            foreach (var validator in checkedListBox_validators.CheckedItems)
                if (_validatorsList.TryGetValue(validator.ToString(), out var v))
                    enabledValidators.Append(v.Method.Name + SplitChar);

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
            _jsonPropertiesCollection = new BlockingCollection<JsonProperty>();

            if (!_runFromCmdLine)
            {
                InitReportsGrid();
                await Task.Run(() =>
                {
                    var reportsCollection = RunValidation(true, _saveTmpFiles).ToList();
                    ProcessReport(reportsCollection, _saveReport);
                }).ConfigureAwait(true);
            }
            else
            {
                var reportsCollection = RunValidation(true, _saveTmpFiles).ToList();
                ProcessReport(reportsCollection, _saveReport);
            }

            FlushLog();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);

            if (!_runFromCmdLine)
                tabControl1.SelectTab(1);

            ActivateUiControls(true);
        }

        private async void Button_validateAll_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_projectPath))
                return;

            ActivateUiControls(false);
            InitReportsGrid(true);

            var collectionFolder = _projectPath;

            var totalReportsCollection = new List<ReportItem>();
            var dirList = new List<string>();
            try
            {
                dirList.AddRange(Directory.GetDirectories(collectionFolder,
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
            var sharedDir = dirList.Where(n => n.EndsWith("\\Shared")).ToArray();
            if (sharedDir.Any())
                dirList.Remove(sharedDir.First());

            foreach (var dir in dirList)
            {
                SetProject(dir, false);

                _jsonPropertiesCollection = new BlockingCollection<JsonProperty>();

                if (!_runFromCmdLine)
                {
                    await Task.Run(() =>
                    {
                        var reportsCollection = RunValidation(false, _saveTmpFiles).ToList();
                        ProcessReport(reportsCollection, _saveReport, false);
                        totalReportsCollection.AddRange(reportsCollection);
                    }).ConfigureAwait(true);
                }
                else
                {
                    var reportsCollection = RunValidation(false, _saveTmpFiles).ToList();
                    ProcessReport(reportsCollection, _saveReport, false);
                    totalReportsCollection.AddRange(reportsCollection);
                }

                FlushLog();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
            }

            SetProject(collectionFolder, false);
            InitReportsGrid(_isCollectionFolder);
            ProcessReport(totalReportsCollection, _saveReport);
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

            var row = dataGridView_report.Rows[e.RowIndex];
            var files = new List<string> { "", "" };
            var fileNames = row.Cells[ReportColumns.FullFileName.ToString()].Value
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
            var pathIds = row.Cells[ReportColumns.JsonPath.ToString()].Value
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
            var fileTypeIds = row.Cells[ReportColumns.FileType.ToString()].Value
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
            var lineIds = row.Cells[ReportColumns.LineId.ToString()].Value
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

            var lineNumbers = new List<int> { -1, -1 };
            lineIds = row.Cells[ReportColumns.LineNumber.ToString()].Value
                .ToString()
                .Replace(Environment.NewLine, SplitChar.ToString())
                .Split(SplitChar);
            n = 0;
            foreach (var token in lineIds)
            {
                if (int.TryParse(token, out var line))
                    lineNumbers[n] = line;
                n++;
            }

            var startPositions = new List<int> { -1, -1 };
            lineIds = row.Cells[ReportColumns.StartPosition.ToString()].Value
                .ToString()
                .Replace(Environment.NewLine, SplitChar.ToString())
                .Split(SplitChar);
            n = 0;
            foreach (var token in lineIds)
            {
                if (int.TryParse(token, out var line))
                    startPositions[n] = line;
                n++;
            }

            var endPositions = new List<int> { -1, -1 };
            lineIds = row.Cells[ReportColumns.EndPosition.ToString()].Value
                .ToString()
                .Replace(Environment.NewLine, SplitChar.ToString())
                .Split(SplitChar);
            n = 0;
            foreach (var token in lineIds)
            {
                if (int.TryParse(token, out var line))
                    endPositions[n] = line;
                n++;
            }

            if (_useVsCode)
            {
                var execParams = "-r -g " + files[0] + ":" + lineNumbers[0];
                VsCodeOpenFile(execParams);
                return;
            }

            // if click direct to LineID - open the collected files
            if (e.ColumnIndex == (int)ReportColumns.LineId
                && !string.IsNullOrEmpty(row.Cells[ReportColumns.LineId.ToString()].Value
                    .ToString())
                && !string.IsNullOrEmpty(row.Cells[ReportColumns.FileType.ToString()].Value
                    .ToString())
                && row.Cells[ReportColumns.ValidationType.ToString()].Value
                    .ToString() != ValidationTypeEnum.Scheme.ToString())
                for (var i = 0; i < 2; i++)
                {
                    if (string.IsNullOrEmpty(fileTypes[i]))
                    {
                        if (_editors[i] != null && !_editors[i].IsDisposed)
                            _editors[i].Close();

                        continue;
                    }

                    if (!File.Exists(fileTypes[i]))
                        continue;

                    if (_showPreview && _editors[i] != null
                        && !_editors[i].IsDisposed
                        && (_editors[i].SingleLineBrackets != false
                            || !_editors[i].Text.EndsWith(fileTypes[i])))
                    {
                        _editors[i].SingleLineBrackets = false;
                        _editors[i].LoadTextFromFile(fileTypes[i]);
                    }
                    else
                    {
                        _editors[i] = new JsonViewer("", "", true)
                        {
                            SingleLineBrackets = false
                        };

                        if (i == 0)
                            _editors[0].Closing += OnClosing1;
                        else
                            _editors[1].Closing += OnClosing2;

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


                    if (lines[i] != -1)
                        _editors[i].PermanentHighlightText(lines[i] + "] ");
                }
            // if click to any column except LineID - open original file
            else
            {
                for (var i = 0; i < 2; i++)
                {
                    if (string.IsNullOrEmpty(files[i]))
                    {
                        if (_editors[i] != null && !_editors[i].IsDisposed)
                            _editors[i].Close();

                        continue;
                    }

                    if (!File.Exists(files[i]))
                        continue;

                    if (_showPreview && _editors[i] != null
                        && !_editors[i].IsDisposed
                        && (_editors[i].SingleLineBrackets != _reformatJson
                            || !_editors[i].Text.EndsWith(files[i])))
                    {
                        _editors[i].SingleLineBrackets = _reformatJson;
                        _editors[i].LoadJsonFromFile(files[i]);
                    }
                    else
                    {
                        _editors[i] = new JsonViewer("", "", true)
                        {
                            SingleLineBrackets = _reformatJson
                        };

                        if (i == 0)
                            _editors[i].Closing += OnClosing1;
                        else
                            _editors[i].Closing += OnClosing2;

                        _editors[i].LoadJsonFromFile(files[i]);
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

                    if (TryGetPositionByPathStr(_editors[i].EditorText, paths[i], out var startPos, out var endPos))
                    {
                        _editors[i].PermanentHighlight(startPos, endPos + 1);
                    }
                    else if (lineNumbers[i] != -1)
                    {
                        _editors[i].PermanentHighlightLines(lineNumbers[i] - 1, 0);
                    }
                    else if (startPositions[i] != -1 && endPositions[i] != -1)
                    {
                        _editors[i].PermanentHighlight(startPositions[i], endPositions[i] + 1);
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

            var ignoreReportsCollection = new List<ReportItem>();
            var ignoreFile = _projectName + "\\" + IgnoreFileName;
            if (File.Exists(ignoreFile))
                ignoreReportsCollection = JsonIo.LoadJson<List<ReportItem>>(ignoreFile);

            var newReport = new ReportItem
            {
                FullFileName = currentRow[(int)ReportColumns.FullFileName].ToString(),
                FileType = currentRow[(int)ReportColumns.FileType].ToString(),
                Message = currentRow[(int)ReportColumns.Message].ToString(),
                JsonPath = currentRow[(int)ReportColumns.JsonPath].ToString(),
                Severity = currentRow[(int)ReportColumns.Severity].ToString(),
                ValidationType = currentRow[(int)ReportColumns.ValidationType].ToString(),
                Source = currentRow[(int)ReportColumns.Source].ToString()
            };
            if (!ignoreReportsCollection.Any(n => n.Equals(newReport)))
            {
                ignoreReportsCollection.Add(newReport);
                //dataGridView_report.Rows.Remove(currentRow);
                _reportTable.Rows.RemoveAt(rowNum);

                if (!JsonIo.SaveJson(ignoreReportsCollection, ignoreFile, true))
                    _textLog.AppendLine("Can't save file: " + ignoreFile);
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
                if (_validatorsList.TryGetValue(validator.ToString(), out var v))
                    _checkedValidators.Add(v.Method.Name);
        }

        private void CheckedListBox_validators_ItemCheck(object sender, ItemCheckEventArgs e)
        {
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
                dataGridView_report.SelectionChanged += DataGridView_report_SelectionChanged;
            else
                dataGridView_report.SelectionChanged -= DataGridView_report_SelectionChanged;
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
                _serverAssembliesPath = folderBrowserDialog1.SelectedPath;
        }

        private void CheckBox_vsCode_CheckedChanged(object sender, EventArgs e)
        {
            _useVsCode = checkBox_vsCode.Checked;
        }

        #endregion

        #region Helpers

        private void RunCommandLine(IEnumerable<string> args)
        {
            _runFromCmdLine = true;
            _saveTmpFiles = false;
            foreach (var param in args)
                if (param == "-i" || param == "/i")
                {
                    _ignoreHttpsError = true;
                }
                else if (param == "-s" || param == "/s")
                {
                    _skipSchemaErrors = true;
                }
                else if (param == "-t" || param == "/t")
                {
                    _saveTmpFiles = true;
                }
                else if (param == "-h" || param == "/h" || param == "-?" || param == "/?")
                {
                    Console.WriteLine(HelpString);
                    Exit();
                }
                else if (string.IsNullOrEmpty(_projectPath))
                {
                    SetProject(param, false);
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
                Button_validateAll_Click(this, EventArgs.Empty);
            else
                Button_validateProject_Click(this, EventArgs.Empty);

            Exit();
        }

        private IEnumerable<ReportItem> RunValidation(bool fullInit, bool saveFile = true)
        {
            var startTime = DateTime.Now;
            _textLog.Append($"Validating {_projectName}... ");
            FlushLog();

            _folderType = GetFolderType(_projectPath);

            // collect default project file list
            var filesList = new List<string>();
            var runValidationReportsCollection = new BlockingCollection<ReportItem>();
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
                    runValidationReportsCollection.Add(report);
                }
                else
                {
                    filesList.Add(fullFileName);
                }
            }

            try
            {
                filesList.AddRange(Directory.GetFiles(_projectPath + "\\pages",
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
                runValidationReportsCollection.Add(report);
            }

            // parse all project files and include imports
            var processedFilesList = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var deserializeFileReportsCollection = new BlockingCollection<ReportItem>();
            var parseJsonObjectReportsCollection = new BlockingCollection<ReportItem>();
            Parallel.ForEach(filesList, fileName =>
            {
                var fileType = Utilities.GetFileTypeFromFileName(fileName, _fileTypes);
                DeserializeFile(fileName,
                    fileType,
                    _jsonPropertiesCollection,
                    processedFilesList,
                    0,
                    deserializeFileReportsCollection,
                    parseJsonObjectReportsCollection);
            });

            // enumerate properties (LineId)
            var id = 0;
            foreach (var item in _jsonPropertiesCollection)
            {
                item.LineId = id;
                id++;
            }

            //save complete collections by fileTypes
            var i1 = 1;
            if (saveFile)
                Parallel.ForEach(_fileTypes, item =>
                {
                    var fileName = GetSaveFileNameByType(item.FileType.ToString());

                    _textLog.AppendLine($"Saving total file: {fileName} [{i1}/{_fileTypes.Count}]");
                    var singleTypeCollection = _jsonPropertiesCollection
                        .Where(n => n.FileType == item.FileType);
                    var typeCollection = singleTypeCollection as JsonProperty[] ?? singleTypeCollection.ToArray();
                    SaveCollectionToFile(typeCollection, fileName);
                    i1++;
                });

            // a fast way to check if user has selected "Apply patches"
            _patchAllFields = _validatorsList.ContainsKey("Apply patches");

            // initialize validator settings
            InitValidator(deserializeFileReportsCollection,
                parseJsonObjectReportsCollection,
                runValidationReportsCollection,
                processedFilesList,
                fullInit);
            // run every validator selected
            var reportsCollection = new BlockingCollection<ReportItem>();
            Parallel.ForEach(_validatorsList, validator =>
            {
                var validatorMethod = validator.Value.Method.Name;
                if (_checkedValidators.Contains(validatorMethod))
                {
                    var report = validator.Value(validatorMethod);
                    foreach (var item in report)
                        reportsCollection.Add(item);
                }
            });

            _textLog.AppendLine(
                $"Files validated: {processedFilesList.Count}, issues found: {reportsCollection.Count}");
            FlushLog();
            return reportsCollection;
        }

        private void ProcessReport(List<ReportItem> reports, bool saveFile = true, bool updateTable = true)
        {
            // get list of globally ignored errors
            var systemIgnoreReportsCollection = JsonIo.LoadJson<List<ReportItem>>(GlobalIgnoreFileName);
            // get list of ignored errors and filter them out from report
            var ignoreFile = _projectName + "\\" + IgnoreFileName;
            var ignoreReportsCollection = new List<ReportItem>();
            if (File.Exists(ignoreFile))
                ignoreReportsCollection = JsonIo.LoadJson<List<ReportItem>>(ignoreFile);

            var jsonContent = JsonIo.LoadJson<List<ReportItem>>(ignoreFile);
            if (jsonContent != null && jsonContent.Count > 0)
                ignoreReportsCollection.AddRange(jsonContent);

            if (ignoreReportsCollection.Any() || systemIgnoreReportsCollection.Any())
                for (var i = 0; i < reports.Count; i++)
                {
                    if (systemIgnoreReportsCollection.Any(n => n.Equals(reports[i])))
                    {
                        reports.RemoveAt(i);
                        i--;
                    }

                    if (ignoreReportsCollection.Any(n => n.Equals(reports[i])))
                    {
                        reports.RemoveAt(i);
                        i--;
                    }
                }

            // transfer report to table
            if (!_runFromCmdLine && updateTable)
                foreach (var reportLine in reports)
                {
                    var newRow = _reportTable.NewRow();
                    newRow[ReportColumns.ProjectName.ToString()] = reportLine.ProjectName ?? "";
                    newRow[ReportColumns.LineId.ToString()] =
                        reportLine.LineId?.Replace(SplitChar.ToString(), Environment.NewLine);
                    newRow[ReportColumns.LineNumber.ToString()] =
                        reportLine.LineNumber?.Replace(SplitChar.ToString(), Environment.NewLine);
                    newRow[ReportColumns.StartPosition.ToString()] =
                        reportLine.StartPosition?.Replace(SplitChar.ToString(), Environment.NewLine);
                    newRow[ReportColumns.EndPosition.ToString()] =
                        reportLine.EndPosition?.Replace(SplitChar.ToString(), Environment.NewLine);
                    newRow[ReportColumns.FileType.ToString()] =
                        reportLine.FileType?.Replace(SplitChar.ToString(), Environment.NewLine);
                    newRow[ReportColumns.Message.ToString()] = reportLine.Message ?? "";
                    newRow[ReportColumns.JsonPath.ToString()] =
                        reportLine.JsonPath?.Replace(SplitChar.ToString(), Environment.NewLine);
                    newRow[ReportColumns.ValidationType.ToString()] = reportLine.ValidationType ?? "";
                    newRow[ReportColumns.Severity.ToString()] = reportLine.Severity ?? "";
                    newRow[ReportColumns.FullFileName.ToString()] =
                        reportLine.FullFileName?.Replace(SplitChar.ToString(), Environment.NewLine);
                    _reportTable.Rows.Add(newRow);
                }

            if (!saveFile)
                return;

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
                _textLog.AppendLine("Can't save file: " + reportFileName);
        }

        private void DeserializeFile(
            string fullFileName,
            JsoncContentType fileType,
            BlockingCollection<JsonProperty> rootCollection,
            ConcurrentDictionary<string, string> processedList,
            int jsonDepth,
            BlockingCollection<ReportItem> deserializeFileReportsCollection,
            BlockingCollection<ReportItem> parseJsonObjectReportsCollection)
        {
            if (processedList.ContainsKey(fullFileName))
                return;

            processedList.TryAdd(fullFileName, "");
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
                deserializeFileReportsCollection.Add(report);

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
                deserializeFileReportsCollection.Add(report);

                return;
            }

            dynamic jsonObject;
            try
            {
                /*var jsonSettings = new JsonSerializerSettings
                {
                    Formatting = Formatting.None,
                    
                };

                jsonObject = JsonConvert.DeserializeObject(jsonStr, jsonSettings);*/

                jsonObject = JObject.Parse(jsonStr,
                new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Load,
                    LineInfoHandling = LineInfoHandling.Load,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
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
                deserializeFileReportsCollection.Add(report);

                return;
            }

            var ver = "";
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
                    shared,
                    deserializeFileReportsCollection,
                    parseJsonObjectReportsCollection);
        }

        private void ParseJsonObject(
            JToken token,
            JsoncContentType fileType,
            BlockingCollection<JsonProperty> rootCollection,
            string fullFileName,
            ConcurrentDictionary<string, string> processedList,
            ref string version,
            int jsonDepth,
            string parent,
            bool shared,
            BlockingCollection<ReportItem> deserializeFileReportsCollection,
            BlockingCollection<ReportItem> parseJsonObjectReportsCollection)
        {
            if (token == null || rootCollection == null)
                return;

            switch (token)
            {
                case JProperty jProperty:
                {
                    var jValue = jProperty.Value;
                    if (jValue is JArray jArrayValue)
                    {
                        var arrayPath = jArrayValue.Path;
                        var arrayName = jProperty.Name;

                        // get new file type
                        if (arrayPath == arrayName && _fileTypes.Any(n => n.PropertyTypeName == arrayName))
                        {
                            fileType = _fileTypes
                               .FirstOrDefault(n => n.PropertyTypeName == arrayName).FileType;
                        }
                    }

                    var jsonPath = jProperty.Path;
                    var propValue = "";
                    var name = jProperty.Name;

                    var lineInfo = (IJsonLineInfo)jProperty;
                    var lineNumber = -1;
                    if (lineInfo != null)
                        lineNumber = ((IJsonLineInfo)jProperty).LineNumber;

                    if (jValue is JValue jPropertyValue)
                    {
                        propValue = jPropertyValue.Value?.ToString();
                    }

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
                                LineNumber = lineNumber.ToString(),
                                Message = $"Scheme version inconsistent: {version}->{propValue}",
                                ValidationType = ValidationTypeEnum.Logic.ToString(),
                                Severity = ImportanceEnum.Error.ToString(),
                                Source = "ParseJsonObject"
                            };
                            parseJsonObjectReportsCollection.Add(report);
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
                        Shared = shared,
                        SourceLineNumber = lineNumber
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
                                LineNumber = lineNumber.ToString(),
                                Message = "Folder type not recognized (Deployment/Repository/...)",
                                ValidationType = ValidationTypeEnum.File.ToString(),
                                Severity = ImportanceEnum.Warning.ToString(),
                                Source = "ParseJsonObject"
                            };
                            parseJsonObjectReportsCollection.Add(report);
                            importFileName = GetFileFromRef(propValue);
                        }
                        else
                        {
                            importFileName = FixFilePath(GetFileFromRef(propValue), fullFileName);
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
                                LineNumber = lineNumber.ToString(),
                                Message = "File doesn't exists",
                                ValidationType = ValidationTypeEnum.File.ToString(),
                                Severity = ImportanceEnum.Error.ToString(),
                                Source = "ParseJsonObject"
                            };
                            parseJsonObjectReportsCollection.Add(report);
                        }
                        else
                        {
                            jsonDepth++;
                            DeserializeFile(
                                importFileName,
                                importFileType,
                                _jsonPropertiesCollection,
                                processedList,
                                jsonDepth,
                                deserializeFileReportsCollection,
                                parseJsonObjectReportsCollection);
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
                                    LineNumber = lineNumber.ToString(),
                                    Message = $"File type inconsistent: {fileType}->{newFileType}",
                                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                                    Severity = ImportanceEnum.Warning.ToString(),
                                    Source = "ParseJsonObject"
                                };
                                parseJsonObjectReportsCollection.Add(report);
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
                                shared,
                                deserializeFileReportsCollection,
                                parseJsonObjectReportsCollection);
                            jsonDepth--;
                        }

                    break;
                }
                case JObject jObject:
                {
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
                            shared,
                            deserializeFileReportsCollection,
                            parseJsonObjectReportsCollection);

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
                            shared,
                            deserializeFileReportsCollection,
                            parseJsonObjectReportsCollection);

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
                        parseJsonObjectReportsCollection.Add(report);
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
            var n = _initialProjectFiles.Select(file => _projectPath + "\\" + file)
                .Count(fullFileName => !File.Exists(fullFileName));

            if (n > _initialProjectFiles.Length / 2)
                _isCollectionFolder = true;


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
                        var reportsCollection = JsonIo.LoadJson<List<ReportItem>>(reportFileName);
                        ProcessReport(reportsCollection, false);
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
            dataGridView_report.SelectionChanged -= DataGridView_report_SelectionChanged;
            dataGridView_report.ClearSelection();
            dataGridView_report.Columns.Clear();

            foreach (var col in Enum.GetNames(typeof(ReportColumns)))
            {
                _reportTable.Columns.Add(col);
                var column = new DataGridViewTextBoxColumn
                {
                    DataPropertyName = col,
                    Name = col,
                    HeaderText = col,
                    AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells,
                };

                if (col == ReportColumns.JsonPath.ToString()
                    || col == ReportColumns.Message.ToString())
                {
                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    column.Width = 50;
                }

                if ((col == ReportColumns.ProjectName.ToString() && !collection)
                    || col == ReportColumns.LineId.ToString()
                    || col == ReportColumns.StartPosition.ToString()
                    || col == ReportColumns.EndPosition.ToString())
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
                dataGridView_report.SelectionChanged += DataGridView_report_SelectionChanged;
        }

        private void SaveCollectionToFile(JsonProperty[] typeCollection, string fileName)
        {
            if (!typeCollection.Any())
                return;

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

            if (_jsonPropertiesCollection == null)
                return;

            var maxId = _jsonPropertiesCollection.LastOrDefault()?.LineId;
            var maxIdLength = maxId.ToString().Length;
            var fileContent = new StringBuilder();
            const int jsonIdent = 4;

            foreach (var property in typeCollection)
            {
                var str = "["
                          + property.LineId.ToString("D" + maxIdLength)
                          + "] "
                          + new string(' ', property.JsonDepth * jsonIdent)
                          + property.Name;
                if (property.ItemType == JsonItemType.Array
                    || property.ItemType == JsonItemType.Object)
                    fileContent.AppendLine(str);
                else
                    fileContent.AppendLine(str + ": \"" + property.Value + "\"");
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

        private void InitValidator(IEnumerable<ReportItem> deserializeFileReportsCollection,
            IEnumerable<ReportItem> parseJsonObjectReportsCollection,
            IEnumerable<ReportItem> runValidationReportsCollection,
            ConcurrentDictionary<string, string> processedFilesList,
            bool fullInit)
        {
            // initialize validator settings
            var procConf = new ProcessConfiguration
            {
                BackupSchemaExtension = BackupSchemaExtension,
                FileMask = FileMask,
                FileTypes = _fileTypes,
                IgnoreHttpsError = _ignoreHttpsError,
                SchemaTag = SchemaTag,
                SkipSchemaErrors = _skipSchemaErrors,
                PatchAllFields = _patchAllFields,
                SplitChar = SplitChar,
                SuppressSchemaErrors = _suppressSchemaErrors,
                SystemDataViews = _systemDataViews,
                SystemMacros = _systemMacros,
                ServerAssembliesPath = _serverAssembliesPath
            };

            var projConf = new ProjectConfiguration
            {
                FolderType = _folderType,
                ProjectName = _projectName,
                ProjectPath = _projectPath
            };

            var seedData = new SeedData
            {
                JsonPropertiesCollection = _jsonPropertiesCollection,
                DeserializeFileReportsCollection = deserializeFileReportsCollection,
                ParseJsonObjectReportsCollection = parseJsonObjectReportsCollection,
                ProcessedFilesList = processedFilesList,
                RunValidationReportsCollection = runValidationReportsCollection
            };

            ProjectValidator.Initialize(procConf, projConf, seedData, fullInit);
        }

        #endregion

        #region Utilities

        private void FlushLog()
        {
            if (_textLog.Length <= 0)
                return;

            if (!_runFromCmdLine)
                Invoke((MethodInvoker)delegate
               {
                   textBox_logText.Text += _textLog.ToString();
                   textBox_logText.SelectionStart = textBox_logText.Text.Length;
                   textBox_logText.ScrollToCaret();
               });
            else
                Console.WriteLine(_textLog.ToString());

            _textLog.Clear();
        }

        private FolderType GetFolderType(string projectPath)
        {
            var folderType = FolderType.Unknown;

            //Project is in the \"\\Deployment\\Server\\Apps\\MetaUI\\\" folder
            if (Directory.Exists(projectPath + "\\..\\shared\\")
                 && projectPath.Contains("\\Deployment\\Server\\Apps\\"))
            {
                folderType = FolderType.Deployment;
            }
            //Project is in the \"\\MetaUI\\\" folder
            else if (Directory.Exists(projectPath + "\\..\\..\\shared\\"))
            {
                folderType = FolderType.Repository;
            }
            //Project is in the \"\\MetaUI\\ICE\\\" folder
            else if (Directory.Exists(projectPath + "\\..\\..\\..\\shared\\")
                     && Utilities.GetShortFileName(projectPath).StartsWith("Ice"))
            {
                folderType = FolderType.IceRepository;
            }

            return folderType;
        }

        private string GetFilePath(string longFileName)
        {
            var i = longFileName.LastIndexOf('\\');
            return longFileName.Substring(0, i + 1);
        }

        private JsoncContentType GetFileTypeFromJsonPath(string jsonPath)
        {
            var typeName = "";

            if (jsonPath.StartsWith(ImportTagName) && jsonPath.EndsWith(FileTagName))
            {
                typeName = jsonPath.Replace("." + FileTagName, "");
                typeName = typeName.Substring(typeName.LastIndexOf('.') + 1);
            }

            return (from item in _fileTypes where typeName == item.PropertyTypeName select item.FileType)
                .FirstOrDefault();
        }

        private string GetSaveFileNameByType(string fileType, bool noExtension = false)
        {
            var fileName = "";
            foreach (var file in _fileTypes)
            {
                if (file.FileType.ToString() != fileType)
                    continue;

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

        private string FixFilePath(string importedFileFullName, string originalFileFullName)
        {
            var fixedFileName = "";

            if (_folderType == FolderType.Deployment) //deployment folder
            {
                fixedFileName = importedFileFullName;
            }
            else // MetaUI folder
            {
                if (_folderType == FolderType.IceRepository)
                {
                    // still in project folder
                    if (originalFileFullName.Contains(_projectPath))
                    {
                        fixedFileName = importedFileFullName;
                        // goes to shared
                        if (fixedFileName.StartsWith(".."))
                        {
                            fixedFileName = "..\\..\\" + fixedFileName;
                        }
                    }
                    // already in shared
                    else
                    {
                        fixedFileName = importedFileFullName;
                    }
                }
                else if (_folderType == FolderType.Repository)
                {
                    // still in project folder
                    if (originalFileFullName.Contains(_projectPath))
                    {
                        fixedFileName = importedFileFullName;
                        // goes to shared
                        if (fixedFileName.StartsWith(".."))
                        {
                            fixedFileName = "..\\" + fixedFileName;
                        }
                    }
                    // already in shared
                    else
                    {
                        fixedFileName = importedFileFullName;
                    }
                }
            }

            return fixedFileName;
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
            // WinForms app
            if (Application.MessageLoop)
                Application.Exit();
            // Console app
            else
                Environment.Exit(1);
        }

        private bool TryGetPositionByPathStr(string json, string path, out int startPos, out int endPos)
        {
            startPos = -1;
            endPos = -1;

            var pathList = ParseJsonToPathList(json.Replace(' ', ' '), out var _, "", '.', true);

            var pathItems = pathList.Where(n => n.Path == path).ToArray();
            if (!pathItems.Any())
                return false;

            startPos = pathItems.Last().StartPosition;
            endPos = pathItems.Last().EndPosition;
            return true;
        }

        private void VsCodeOpenFile(string command)
        {
            ProcessStartInfo ProcessInfo;
            Process Process;

            ProcessInfo = new ProcessStartInfo("code", command);
            ProcessInfo.CreateNoWindow = true;
            ProcessInfo.UseShellExecute = true;
            ProcessInfo.WindowStyle = ProcessWindowStyle.Hidden;

            try
            {
                Process = Process.Start(ProcessInfo);
            }
            catch (Exception Ex)
            {
                textBox_logText.Text += Ex.Message;
            }
        }

        #endregion

    }
}
