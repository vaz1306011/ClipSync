namespace ClipSync.WindowsApp;

/// <summary>
/// Visual layout for ClipSync settings. The controls are intentionally UI-only;
/// their behavior can be connected as each setting is implemented.
/// </summary>
internal sealed class SettingsForm : Form
{
    public SettingsForm()
    {
        Text = "ClipSync の設定";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(680, 550);
        Font = new Font("Segoe UI", 9F);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
        var general = CreatePage(tabs, "一般");
        var sync = CreatePage(tabs, "同期");
        var otp = CreatePage(tabs, "認証コード");
        var connection = CreatePage(tabs, "接続");

        AddSection(general, "起動", Check("Windows へのサインイン時に ClipSync を起動する"));
        AddSection(general, "通知", LabeledCombo("通知方法", "トースト通知", "入力欄の近くにボタン", "両方"));
        AddSection(general, "認証コードの表示", Check("コードを自動でコピーする"), LabeledNumber("表示時間 (秒)", 60, 5, 300));

        AddSection(sync, "クリップボード", Check("同期を有効にする"), Check("テキスト"), Check("画像"), Check("ファイル"));
        AddSection(sync, "ファイル", LabeledNumber("サイズ上限 (MB、0 は無制限)", 0, 0, 2048));
        AddSection(sync, "除外", Check("クリップボード監視の処理から除外する"));

        AddSection(otp, "検出", Check("認証コードの検出を有効にする"), LabeledNumber("コードの最小文字数", 4, 1, 32), LabeledNumber("コードの最大文字数", 8, 1, 32));
        AddSection(otp, "キーワード", LabeledMultiline("キーワード (1 行に 1 つ)", "認証\n確認コード\ncode\nverification", 72));
        AddSection(otp, "詳細", LabeledText("追加の正規表現"), Check("検出前に文字を NFKC 正規化する"), Check("検出前に URL を除外する"), Button("正規表現をテスト"));

        AddSection(connection, "サーバー", LabeledNumber("ポート番号", 913, 1, 65535), LabeledText("共有キー", "••••••••••••"));
        AddSection(connection, "ペアリング", Button("QR コードを表示"), Button("共有キーを再生成"));

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 52,
            Padding = new Padding(10, 7, 12, 7),
            WrapContents = false
        };
        buttons.Controls.Add(new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 96, Height = 30 });
        buttons.Controls.Add(new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Width = 96, Height = 30 });
        buttons.Controls.Add(new Button { Text = "適用", Enabled = false, Width = 96, Height = 30 });

        Controls.Add(tabs);
        Controls.Add(buttons);
        AcceptButton = buttons.Controls[0] as IButtonControl;
        CancelButton = buttons.Controls[1] as IButtonControl;
    }

    private static FlowLayoutPanel CreatePage(TabControl tabs, string title)
    {
        var page = new TabPage(title) { AutoScroll = true, Padding = new Padding(12) };
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(4)
        };
        page.Controls.Add(content);
        tabs.TabPages.Add(page);
        return content;
    }

    private static void AddSection(Control parent, string title, params Control[] controls)
    {
        var section = new GroupBox
        {
            Text = title,
            Width = 590,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10),
            Margin = new Padding(4, 4, 4, 8)
        };
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        foreach (var control in controls)
        {
            control.Margin = new Padding(4, 5, 4, 5);
            content.Controls.Add(control);
        }
        section.Controls.Add(content);
        parent.Controls.Add(section);
    }

    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true };

    private static Control LabeledNumber(string label, int value, int min, int max)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 5, 12, 0), Width = 270 });
        row.Controls.Add(new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 120, ThousandsSeparator = true });
        return row;
    }

    private static Control LabeledCombo(string label, params string[] choices)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 5, 12, 0), Width = 270 });
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        combo.Items.AddRange(choices);
        combo.SelectedIndex = 0;
        row.Controls.Add(combo);
        return row;
    }

    private static Control LabeledText(string label, string value = "")
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 5, 12, 0), Width = 270 });
        row.Controls.Add(new TextBox { Text = value, Width = 300 });
        return row;
    }

    private static Control LabeledMultiline(string label, string value, int height)
    {
        var stack = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        stack.Controls.Add(new Label { Text = label, AutoSize = true });
        stack.Controls.Add(new TextBox
        {
            Text = value,
            Width = 540,
            Height = height,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true
        });
        return stack;
    }

    private static Button Button(string text) => new() { Text = text, AutoSize = true };
}
