// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++, C#, and Java: http://www.viva64.com

using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

using ScintillaNET;

using ScintillaNETviewer.Utils;

namespace KineticValidator
{
    public partial class JsonViewer : Form
    {
        /// <summary>
        ///     the background color of the text area
        /// </summary>
        private readonly Color BACK_COLOR = Color.LightGray;

        /// <summary>
        ///     default text color of the text area
        /// </summary>
        private readonly Color FORE_COLOR = Color.Black;

        /// <summary>
        ///     change this to whatever margin you want the line numbers to show in
        /// </summary>
        private const int NUMBER_MARGIN = 1;

        /// <summary>
        ///     change this to whatever margin you want the bookmarks/breakpoints to show in
        /// </summary>
        private const int BOOKMARK_MARGIN = 2;

        private const int BOOKMARK_MARKER = 2;

        /// <summary>
        ///     change this to whatever margin you want the code folding tree (+/-) to show in
        /// </summary>
        private const int FOLDING_MARGIN = 3;

        /// <summary>
        ///     set this true to show circular buttons for code folding (the [+] and [-] buttons on the margin)
        /// </summary>
        private const bool CODEFOLDING_CIRCULAR = true;

        // Indicators 0-7 could be in use by a lexer
        // so we'll use indicator 8 to highlight words.
        private const int INDICATOR_NUM = 8;
        private const int PERMANENT_INDICATOR_NUM = 9;

        private Scintilla _textArea = new Scintilla();
        private readonly string _text = "";
        private string _fileName = "";
        public bool SingleLineBrackets = false;
        private bool _multipleSearchActive;
        private bool _textChanged;

        public string EditorText
        {
            get => _textArea.Text;
            set => _textArea.Text = value;
        }

        public bool WordWrap
        {
            get => wordWrapItem.Checked;
            set
            {
                wordWrapItem.Checked = value;
                _textArea.WrapMode = wordWrapItem.Checked ? WrapMode.Word : WrapMode.None;
            }
        }

        public bool IndentGuides
        {
            get => indentGuidesItem.Checked;
            set
            {
                indentGuidesItem.Checked = value;
                _textArea.IndentationGuides = indentGuidesItem.Checked ? IndentView.LookBoth : IndentView.None;
            }
        }

        public bool ShowWhiteSpace
        {
            get => hiddenCharactersItem.Checked;
            set
            {
                hiddenCharactersItem.Checked = value;
                _textArea.ViewWhitespace = hiddenCharactersItem.Checked
                    ? WhitespaceMode.VisibleAlways
                    : WhitespaceMode.Invisible;
            }
        }

        public bool AlwaysOnTop
        {
            get => TopMost;
            set
            {
                alwaysOnTopToolStripMenuItem.Checked = value;
                TopMost = alwaysOnTopToolStripMenuItem.Checked;
            }
        }

        public JsonViewer(bool allowSave = false)
        {
            InitializeComponent();
            saveToolStripMenuItem.Visible = allowSave;
        }

        public JsonViewer(string fileName, string text, bool allowSave = false)
        {
            InitializeComponent();
            saveToolStripMenuItem.Visible = allowSave;
            _fileName = fileName;
            _text = text;
            _textArea.MultipleSelection = true;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // CREATE CONTROL
            _textArea = new Scintilla();
            TextPanel.Controls.Add(_textArea);

            // BASIC CONFIG
            _textArea.Dock = DockStyle.Fill;

            // INITIAL VIEW CONFIG
            _textArea.WrapMode = WrapMode.None;
            _textArea.IndentationGuides = IndentView.LookBoth;

            // STYLING
            InitColors();
            InitSyntaxColoring();

            // NUMBER MARGIN
            InitNumberMargin();

            // BOOKMARK MARGIN
            InitBookmarkMargin();

            // CODE FOLDING MARGIN
            InitCodeFolding();

            // DEFAULT FILE
            if (string.IsNullOrEmpty(_text))
            {
                LoadTextFromFile(_fileName);
            }
            else
            {
                Text += _fileName;
                _textArea.Text = _text;
            }

            // INIT HOTKEYS
            InitHotkeys();

            _textArea.MouseDoubleClick += SelectAllPatterns;
            _textArea.TextChanged += TextChangedFlag;
            this.FormClosing += SaveOnFormClosing;

        }

        private void SaveOnFormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = !ConfirmOnCloseFile();
        }

        private bool ConfirmOnCloseFile()
        {
            if (!saveToolStripMenuItem.Visible)
                return true;

            if (!_textChanged)
                return true;

            var result = MessageBox.Show("Do you want to save file?", "File has been changed", MessageBoxButtons.YesNoCancel);

            if (result == DialogResult.Yes)
            {
                if (!SaveTextToFile(_fileName, true, false))
                {
                    MessageBox.Show("Failed to save file!");
                    return false;
                }
            }
            else if (result == DialogResult.No)
            {

            }
            else if (result == DialogResult.Cancel)
            {
                return false;
            }

            return true;
        }

        private void TextChangedFlag(object sender, EventArgs e)
        {
            _textChanged = true;
            _textArea.TextChanged -= TextChangedFlag;
        }

        private void InitColors()
        {
            _textArea.SetSelectionBackColor(true, Color.DodgerBlue);
        }

        private void InitHotkeys()
        {
            // remove conflicting hotkeys from scintilla
            _textArea.ClearCmdKey(Keys.Control | Keys.F);
            _textArea.ClearCmdKey(Keys.Control | Keys.R);
            _textArea.ClearCmdKey(Keys.Control | Keys.H);
            _textArea.ClearCmdKey(Keys.Control | Keys.L);
            _textArea.ClearCmdKey(Keys.Control | Keys.U);
            _textArea.ClearCmdKey(Keys.Control | Keys.S);
            _textArea.ClearCmdKey(Keys.Control | Keys.O);
            _textArea.ClearCmdKey(Keys.Alt | Keys.S);
            _textArea.ClearCmdKey(Keys.Alt | Keys.O);

            // register the hotkeys with the form
            HotKeyManager.AddHotKey(this, OpenSearch, Keys.F, true);
            HotKeyManager.AddHotKey(this, Uppercase, Keys.Up, true);
            HotKeyManager.AddHotKey(this, Lowercase, Keys.Down, true);
            HotKeyManager.AddHotKey(this, ZoomIn, Keys.Oemplus, true);
            HotKeyManager.AddHotKey(this, ZoomOut, Keys.OemMinus, true);
            HotKeyManager.AddHotKey(this, ZoomDefault, Keys.D0, true);
            HotKeyManager.AddHotKey(this, CloseSearch, Keys.Escape);
            HotKeyManager.AddHotKey(this, CollapseAll, Keys.Left, true);
            HotKeyManager.AddHotKey(this, ExpandAll, Keys.Right, true);
            HotKeyManager.AddHotKey(this, FormatText, Keys.F, true, false, true);
            HotKeyManager.AddHotKey(this, SaveFile, Keys.S, false, false, true);
            HotKeyManager.AddHotKey(this, OpenFile, Keys.O, false, false, true);
        }

        private void InitSyntaxColoring()
        {
            // Configure the default style
            _textArea.StyleResetDefault();
            _textArea.Styles[Style.Default].Font = "Consolas";
            _textArea.Styles[Style.Default].Size = 12;
            _textArea.Styles[Style.Default].BackColor = BACK_COLOR;
            _textArea.Styles[Style.Default].ForeColor = FORE_COLOR;
            _textArea.StyleClearAll();

            _textArea.Styles[Style.Json.Default].ForeColor = FORE_COLOR;
            _textArea.Styles[Style.Json.BlockComment].ForeColor = Color.DarkGray;
            _textArea.Styles[Style.Json.CompactIRI].ForeColor = Color.White;
            _textArea.Styles[Style.Json.Error].ForeColor = Color.OrangeRed;
            _textArea.Styles[Style.Json.EscapeSequence].ForeColor = Color.Orange;
            _textArea.Styles[Style.Json.Keyword].ForeColor = Color.White;
            _textArea.Styles[Style.Json.LdKeyword].ForeColor = Color.DarkGreen;
            _textArea.Styles[Style.Json.LineComment].ForeColor = Color.DarkGray;
            _textArea.Styles[Style.Json.Number].ForeColor = Color.Blue;
            _textArea.Styles[Style.Json.Operator].ForeColor = Color.Magenta;
            _textArea.Styles[Style.Json.PropertyName].ForeColor = Color.Green;
            _textArea.Styles[Style.Json.String].ForeColor = Color.Sienna;
            _textArea.Styles[Style.Json.StringEol].ForeColor = Color.Black;
            _textArea.Styles[Style.Json.Uri].ForeColor = Color.DarkBlue;

            _textArea.Lexer = Lexer.Json;
        }

        #region Numbers, Bookmarks, Code Folding

        private void InitNumberMargin()
        {
            _textArea.Styles[Style.LineNumber].BackColor = BACK_COLOR;
            _textArea.Styles[Style.LineNumber].ForeColor = FORE_COLOR;
            _textArea.Styles[Style.IndentGuide].ForeColor = FORE_COLOR;
            _textArea.Styles[Style.IndentGuide].BackColor = BACK_COLOR;

            var nums = _textArea.Margins[NUMBER_MARGIN];
            nums.Width = 40;
            nums.Type = MarginType.Number;
            nums.Sensitive = true;
            nums.Mask = 0;

            _textArea.MarginClick += TextArea_MarginClick;
        }

        private void InitBookmarkMargin()
        {
            //TextArea.SetFoldMarginColor(true, BACK_COLOR);

            var margin = _textArea.Margins[BOOKMARK_MARGIN];
            margin.Width = 0;
            margin.Sensitive = true;
            margin.Type = MarginType.Symbol;
            margin.Mask = 1 << BOOKMARK_MARKER;
            //margin.Cursor = MarginCursor.Arrow;

            var marker = _textArea.Markers[BOOKMARK_MARKER];
            marker.Symbol = MarkerSymbol.Circle;
            marker.SetBackColor(Color.Red);
            marker.SetForeColor(BACK_COLOR);
            marker.SetAlpha(100);
        }

        private void InitCodeFolding()
        {
            _textArea.SetFoldMarginColor(true, BACK_COLOR);
            _textArea.SetFoldMarginHighlightColor(true, BACK_COLOR);

            // Enable code folding
            _textArea.SetProperty("fold", "1");
            _textArea.SetProperty("fold.compact", "1");

            // Configure a margin to display folding symbols
            _textArea.Margins[FOLDING_MARGIN].Type = MarginType.Symbol;
            _textArea.Margins[FOLDING_MARGIN].Mask = Marker.MaskFolders;
            _textArea.Margins[FOLDING_MARGIN].Sensitive = true;
            _textArea.Margins[FOLDING_MARGIN].Width = 20;

            // Set colors for all folding markers
            for (var i = 25; i <= 31; i++)
            {
                _textArea.Markers[i].SetForeColor(BACK_COLOR); // styles for [+] and [-]
                _textArea.Markers[i].SetBackColor(FORE_COLOR); // styles for [+] and [-]
            }

            // Configure folding markers with respective symbols
            _textArea.Markers[Marker.Folder].Symbol =
                CODEFOLDING_CIRCULAR ? MarkerSymbol.CirclePlus : MarkerSymbol.BoxPlus;
            _textArea.Markers[Marker.FolderOpen].Symbol =
                CODEFOLDING_CIRCULAR ? MarkerSymbol.CircleMinus : MarkerSymbol.BoxMinus;
            _textArea.Markers[Marker.FolderEnd].Symbol = CODEFOLDING_CIRCULAR
                ? MarkerSymbol.CirclePlusConnected
                : MarkerSymbol.BoxPlusConnected;
            _textArea.Markers[Marker.FolderMidTail].Symbol = MarkerSymbol.TCorner;
            _textArea.Markers[Marker.FolderOpenMid].Symbol = CODEFOLDING_CIRCULAR
                ? MarkerSymbol.CircleMinusConnected
                : MarkerSymbol.BoxMinusConnected;
            _textArea.Markers[Marker.FolderSub].Symbol = MarkerSymbol.VLine;
            _textArea.Markers[Marker.FolderTail].Symbol = MarkerSymbol.LCorner;

            // Enable automatic folding
            _textArea.AutomaticFold = AutomaticFold.Show | AutomaticFold.Click | AutomaticFold.Change;
        }

        private void TextArea_MarginClick(object sender, MarginClickEventArgs e)
        {
            if (e.Margin != BOOKMARK_MARGIN)
                return;

            // Do we have a marker for this line?
            const uint mask = 1 << BOOKMARK_MARKER;
            var line = _textArea.Lines[_textArea.LineFromPosition(e.Position)];
            if ((line.MarkerGet() & mask) > 0)
                // Remove existing bookmark
                line.MarkerDelete(BOOKMARK_MARKER);
            else
                // Add bookmark
                line.MarkerAdd(BOOKMARK_MARKER);
        }

        #endregion

        #region Load Save File

        public bool LoadTextFromFile(string fullFileName)
        {
            if (!ConfirmOnCloseFile())
                return false;

            if (string.IsNullOrEmpty(fullFileName))
                return false;

            if (!File.Exists(fullFileName))
            {
                MessageBox.Show("File not found: " + fullFileName);
                return false;
            }

            Text += fullFileName;
            _fileName = fullFileName;

            var fileContent = "";
            try
            {
                fileContent = File.ReadAllText(fullFileName);
            }
            catch (Exception)
            {

            }

            _textArea.Text = fileContent;

            _textArea.TextChanged -= TextChangedFlag;
            _textChanged = false;
            _textArea.TextChanged += TextChangedFlag;

            return true;
        }

        public bool LoadJsonFromFile(string fullFileName)
        {
            if (!ConfirmOnCloseFile())
                return false;

            if (string.IsNullOrEmpty(fullFileName))
                return false;

            if (!File.Exists(fullFileName))
            {
                MessageBox.Show("File not found: " + fullFileName);
                return false;
            }

            Text += fullFileName;
            _fileName = fullFileName;

            var fileContent = "";
            try
            {
                fileContent = File.ReadAllText(fullFileName);
            }
            catch (Exception)
            {

            }

            _textArea.Text = SingleLineBrackets
                ? JsonIo.BeautifyJson(fileContent, SingleLineBrackets)
                : fileContent;

            _textArea.TextChanged -= TextChangedFlag;
            _textChanged = SingleLineBrackets;

            if (!_textChanged)
                _textArea.TextChanged += TextChangedFlag;

            return true;
        }

        public bool SaveTextToFile(string fullFileName, bool makeBackup = true, bool showDialog = true)
        {
            if (string.IsNullOrEmpty(fullFileName))
            {
                return false;
            }

            if (showDialog)
            {
                var choice = MessageBox.Show("Do you want to save file?", "File has been changed", MessageBoxButtons.YesNo);
                if (choice == DialogResult.No)
                {
                    return false;
                }
            }

            try
            {
                if (makeBackup)
                {
                    var bakFileName = fullFileName + ".bak";

                    if (File.Exists(fullFileName))
                    {
                        if (File.Exists(bakFileName))
                            File.Delete(bakFileName);
                        File.Move(fullFileName, bakFileName);
                    }
                }

                File.WriteAllText(fullFileName, _textArea.Text);
            }
            catch (Exception)
            {
                return false;
            }

            _textArea.TextChanged -= TextChangedFlag;
            _textChanged = false;
            _textArea.TextChanged += TextChangedFlag;

            return true;
        }

        #endregion

        #region Main Menu Commands

        private void FindToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenSearch();
        }

        private void OpenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFile();
        }

        private void SaveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFile();
        }

        private void OpenFile()
        {
            if (!ConfirmOnCloseFile())
                return;

            openFileDialog.FileName = "";
            openFileDialog.Title = "Open file";
            openFileDialog.DefaultExt = "jsonc";
            openFileDialog.Filter = "Text files|*.txt;*.jsonc;*.json|All files|*.*";
            if (openFileDialog.ShowDialog() == DialogResult.OK)
                LoadTextFromFile(openFileDialog.FileName);
        }

        private void SaveFile()
        {
            SaveTextToFile(_fileName);
        }

        private void CutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _textArea.Cut();
        }

        private void CopyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _textArea.Copy();
        }

        private void PasteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _textArea.Paste();
        }

        private void SelectAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _textArea.SelectAll();
        }

        private void SelectLineToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var line = _textArea.Lines[_textArea.CurrentLine];
            _textArea.SetSelection(line.Position + line.Length, line.Position);
        }

        private void ClearSelectionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _textArea.SetEmptySelection(0);
        }

        private void IndentSelectionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Indent();
        }

        private void OutdentSelectionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Outdent();
        }

        private void UppercaseSelectionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Uppercase();
        }

        private void LowercaseSelectionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Lowercase();
        }

        private void WordWrapToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            // toggle word wrap
            wordWrapItem.Checked = !wordWrapItem.Checked;
            _textArea.WrapMode = wordWrapItem.Checked ? WrapMode.Word : WrapMode.None;
        }

        private void IndentGuidesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // toggle indent guides
            indentGuidesItem.Checked = !indentGuidesItem.Checked;
            _textArea.IndentationGuides = indentGuidesItem.Checked ? IndentView.LookBoth : IndentView.None;
        }

        private void FormatToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FormatText();
        }

        private void FormatText()
        {
            _textArea.Text = JsonIo.BeautifyJson(_textArea.Text, SingleLineBrackets);
            _textArea.SelectionStart = _textArea.SelectionEnd = 0;
            _textArea.ScrollCaret();
        }

        private void HiddenCharactersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // toggle view whitespace
            hiddenCharactersItem.Checked = !hiddenCharactersItem.Checked;
            _textArea.ViewWhitespace =
                hiddenCharactersItem.Checked ? WhitespaceMode.VisibleAlways : WhitespaceMode.Invisible;
        }

        private void ZoomInToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ZoomIn();
        }

        private void ZoomOutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ZoomOut();
        }

        private void Zoom100ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ZoomDefault();
        }

        private void CollapseAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CollapseAll();
        }

        private void ExpandAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExpandAll();
        }

        private void CollapseAll()
        {
            _textArea.FoldAll(FoldAction.Contract);
        }

        private void ExpandAll()
        {
            _textArea.FoldAll(FoldAction.Expand);
        }

        private void AlwaysOnTopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            alwaysOnTopToolStripMenuItem.Checked = !alwaysOnTopToolStripMenuItem.Checked;
            TopMost = alwaysOnTopToolStripMenuItem.Checked;
        }

        #endregion

        #region Uppercase / Lowercase

        private void Lowercase()
        {
            // save the selection
            var start = _textArea.SelectionStart;
            var end = _textArea.SelectionEnd;

            // modify the selected text
            _textArea.ReplaceSelection(_textArea.GetTextRange(start, end - start).ToLower());

            // preserve the original selection
            _textArea.SetSelection(start, end);
        }

        private void Uppercase()
        {
            // save the selection
            var start = _textArea.SelectionStart;
            var end = _textArea.SelectionEnd;

            // modify the selected text
            _textArea.ReplaceSelection(_textArea.GetTextRange(start, end - start).ToUpper());

            // preserve the original selection
            _textArea.SetSelection(start, end);
        }

        #endregion

        #region Indent / Outdent

        private void Indent()
        {
            // we use this hack to send "Shift+Tab" to scintilla, since there is no known API to indent,
            // although the indentation function exists. Pressing TAB with the editor focused confirms this.
            GenerateKeystrokes("{TAB}");
        }

        private void Outdent()
        {
            // we use this hack to send "Shift+Tab" to scintilla, since there is no known API to outdent,
            // although the indentation function exists. Pressing Shift+Tab with the editor focused confirms this.
            GenerateKeystrokes("+{TAB}");
        }

        private void GenerateKeystrokes(string keys)
        {
            HotKeyManager.Enable = false;
            _textArea.Focus();
            SendKeys.Send(keys);
            HotKeyManager.Enable = true;
        }

        #endregion

        #region Zoom

        private void ZoomIn()
        {
            _textArea.ZoomIn();
        }

        private void ZoomOut()
        {
            _textArea.ZoomOut();
        }

        private void ZoomDefault()
        {
            _textArea.Zoom = 0;
        }

        #endregion

        #region Quick Search Bar

        private bool _searchIsOpen;

        private void OpenSearch()
        {
            SearchManager.SearchBox = TxtSearch;
            SearchManager.TextArea = _textArea;

            if (!_searchIsOpen)
            {
                _searchIsOpen = true;
                InvokeIfNeeded(delegate
                {
                    PanelSearch.Visible = true;
                    TxtSearch.Text = SearchManager.LastSearch;
                    TxtSearch.Focus();
                    TxtSearch.SelectAll();
                });
            }
            else
            {
                InvokeIfNeeded(delegate
                {
                    TxtSearch.Focus();
                    TxtSearch.SelectAll();
                });
            }
        }

        private void CloseSearch()
        {
            if (!_searchIsOpen)
            {
                if (_multipleSearchActive)
                {
                    _textArea.IndicatorCurrent = INDICATOR_NUM;
                    _textArea.IndicatorClearRange(0, _textArea.TextLength);
                    return;
                }

                Close();
                return;
            }

            _searchIsOpen = false;
            InvokeIfNeeded(delegate
            {
                PanelSearch.Visible = false;
                //CurBrowser.GetBrowser().StopFinding(true);
            });
        }

        private void BtnCloseSearch_Click(object sender, EventArgs e)
        {
            CloseSearch();
        }

        private void BtnPrevSearch_Click(object sender, EventArgs e)
        {
            SearchManager.Find(false, false);
        }

        private void BtnNextSearch_Click(object sender, EventArgs e)
        {
            SearchManager.Find(true, false);
        }

        private void TxtSearch_TextChanged(object sender, EventArgs e)
        {
            SearchManager.Find(true, true);
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (HotKeyManager.IsHotkey(e, Keys.Enter))
                SearchManager.Find(true, false);
            if (HotKeyManager.IsHotkey(e, Keys.Enter, true) || HotKeyManager.IsHotkey(e, Keys.Enter, false, true))
                SearchManager.Find(false, false);
        }

        private void SelectAllPatterns(object sender, MouseEventArgs e)
        {
            HighlightWord(_textArea.SelectedText);
        }

        private void HighlightWord(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Remove all uses of our indicator
            _textArea.IndicatorCurrent = INDICATOR_NUM;
            _textArea.IndicatorClearRange(0, _textArea.TextLength);

            // Update indicator appearance
            _textArea.Indicators[INDICATOR_NUM].Style = IndicatorStyle.StraightBox;
            _textArea.Indicators[INDICATOR_NUM].Under = true;
            _textArea.Indicators[INDICATOR_NUM].ForeColor = Color.DarkGreen;
            _textArea.Indicators[INDICATOR_NUM].OutlineAlpha = 50;
            _textArea.Indicators[INDICATOR_NUM].Alpha = 50;

            // Search the document
            _textArea.TargetStart = 0;
            _textArea.TargetEnd = _textArea.TextLength;
            _textArea.SearchFlags = SearchFlags.None;
            while (_textArea.SearchInTarget(text) != -1)
            {
                // Mark the search results with the current indicator
                _textArea.IndicatorFillRange(_textArea.TargetStart, _textArea.TargetEnd - _textArea.TargetStart);

                // Search the remainder of the document
                _textArea.TargetStart = _textArea.TargetEnd;
                _textArea.TargetEnd = _textArea.TextLength;
            }

            _multipleSearchActive = true;
        }


        #endregion

        #region Utils

        private void InvokeIfNeeded(Action action)
        {
            if (InvokeRequired)
                BeginInvoke(action);
            else
                action.Invoke();
        }

        public void SelectText(string text)
        {
            if (FindTextLines(_textArea.Text, text, out var startLine, out var lineNum))
                SelectTextLines(startLine, lineNum);
        }

        public void PermanentHighlightText(string text)
        {
            if (FindTextLines(_textArea.Text, text, out var startLine, out var lineNum))
                PermanentHighlightLines(startLine, lineNum);
        }

        private static bool FindTextLines(string text, string sample, out int startLine, out int lineNum)
        {
            startLine = 0;
            lineNum = 0;
            var compactText = JsonIo.TrimJson(text, true);
            var compactSample = JsonIo.TrimJson(sample, true);
            var startIndex = compactText.IndexOf(compactSample, StringComparison.Ordinal);
            if (startIndex < 0)
                return false;

            startLine = CountLines(compactText, 0, startIndex);
            lineNum = CountLines(compactText, startIndex, startIndex + compactSample.Length);

            return true;
        }

        private static int CountLines(string text, int startIndex, int endIndex)
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

        public void SelectTextLines(int lineStart, int lineNum)
        {
            if (lineStart < 0 || lineStart >= _textArea.Lines.Count || lineNum <= 0)
                return;

            var startLine = _textArea.Lines[lineStart];
            var endLine = _textArea.Lines[lineStart + lineNum - 1];
            _textArea.SetSelection(startLine.Position, endLine.Position + endLine.Length);

            _textArea.ScrollCaret();
        }

        public void SelectPosition(int start, int end)
        {
            if (start < 0 || start >= _textArea.TextLength)
                return;
            if (end < 0 || end >= _textArea.TextLength)
                return;

            _textArea.SelectionStart = start;
            _textArea.SelectionEnd = end;
            _textArea.ScrollCaret();
        }

        public void PermanentHighlight(int start, int end)
        {
            if (start < 0 || start >= _textArea.TextLength)
                return;
            if (end < 0 || end >= _textArea.TextLength)
                return;

            // Remove all uses of our indicator
            _textArea.IndicatorCurrent = PERMANENT_INDICATOR_NUM;
            _textArea.IndicatorClearRange(0, _textArea.TextLength);

            // Update indicator appearance
            _textArea.Indicators[PERMANENT_INDICATOR_NUM].Style = IndicatorStyle.RoundBox;
            _textArea.Indicators[PERMANENT_INDICATOR_NUM].Under = true;
            _textArea.Indicators[PERMANENT_INDICATOR_NUM].ForeColor = Color.DarkRed;
            _textArea.Indicators[PERMANENT_INDICATOR_NUM].OutlineAlpha = 100;
            _textArea.Indicators[PERMANENT_INDICATOR_NUM].Alpha = 50;

            // Mark the search results with the current indicator
            _textArea.IndicatorFillRange(start, end - start);
            _textArea.ScrollRange(start, end);
            //_textArea.LineScroll(5, 0);

            _multipleSearchActive = true;
        }

        public void PermanentHighlightLines(int startLine, int linesNumber)
        {
            if (startLine < 0 || startLine >= _textArea.TextLength)
                return;
            if (linesNumber < 0 || linesNumber >= _textArea.TextLength)
                return;

            // Remove all uses of our indicator
            _textArea.IndicatorCurrent = PERMANENT_INDICATOR_NUM;
            _textArea.IndicatorClearRange(0, _textArea.TextLength);

            // Update indicator appearance
            _textArea.Indicators[PERMANENT_INDICATOR_NUM].Style = IndicatorStyle.RoundBox;
            _textArea.Indicators[PERMANENT_INDICATOR_NUM].Under = true;
            _textArea.Indicators[PERMANENT_INDICATOR_NUM].ForeColor = Color.DarkRed;
            _textArea.Indicators[PERMANENT_INDICATOR_NUM].OutlineAlpha = 100;
            _textArea.Indicators[PERMANENT_INDICATOR_NUM].Alpha = 50;

            // Mark the search results with the current indicator
            var startPosition = _textArea.Lines[startLine].Position;
            var endPosition = _textArea.Lines[startLine + linesNumber].EndPosition;


            _textArea.IndicatorFillRange(startPosition, endPosition - startPosition);
            _textArea.ScrollRange(startPosition, endPosition);

            _multipleSearchActive = true;
        }

        #endregion
    }
}
