using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ScreenCap.Theme;

namespace ScreenCap.Help;

/// <summary>In-app user manual, opened from the tray menu.</summary>
internal sealed class HelpWindow : Window
{
    private static HelpWindow? _instance;

    private readonly StackPanel _body = new() { Margin = new Thickness(28, 20, 28, 28) };

    private HelpWindow()
    {
        Title = "ScreenCap 使用說明";
        Width = 640;
        Height = 760;
        MinWidth = 480;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ThemeManager.Attach(this);
        FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft JhengHei UI, Segoe UI");
        FontSize = 14;
        UseLayoutRounding = true;
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        BuildContent();
    }

    public static void ShowManual()
    {
        if (_instance == null)
        {
            _instance = new HelpWindow();
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }
        if (_instance.WindowState == WindowState.Minimized)
            _instance.WindowState = WindowState.Normal;
        _instance.Activate();
    }

    private void BuildContent()
    {
        Heading("ScreenCap 使用說明", 24, top: 0);
        Paragraph("ScreenCap 是常駐在系統匣的截圖工具，可以框選螢幕區域、加上標註，再複製、存檔或貼到桌面上。");

        Section("快速開始");
        Shortcuts(
            ("F1", "開始截圖"),
            ("拖曳滑鼠", "框選要截取的範圍"),
            ("Enter", "複製到剪貼簿並結束（可在設定中改成存檔或貼圖）"),
            ("Shift + F1", "錄製螢幕（GIF / MP4）；錄製中再按一次即停止"),
            ("F3", "把剪貼簿中的圖片（或文字、色碼）貼到桌面"),
            ("Ctrl + Shift + F1", "再次開啟上一張截圖，標註都還能修改"));
        Paragraph("以上是預設熱鍵，可以在系統匣選單的「設定」中更改。");

        Section("選取區域");
        Paragraph("按下 F1 後畫面會凍結並變暗。移動滑鼠時會自動框出游標下的視窗或控制項，單擊即可採用；也可以直接拖曳出自訂範圍。");
        Shortcuts(
            ("單擊", "採用自動偵測到的視窗範圍"),
            ("拖曳", "自訂選取範圍"),
            ("拖曳控制點", "調整選取範圍大小"),
            ("拖曳選區內部", "移動選取範圍"),
            ("方向鍵", "選取前：游標移動 1 像素；選取後：選區移動 1 像素"),
            ("Shift + 方向鍵", "選區大小增減 1 像素"),
            ("C", "選取前：複製游標下的色值"),
            ("Shift", "選取前：切換複製的色值格式（HEX / RGB）"),
            ("Ctrl + A", "選取游標所在的整個螢幕；再按一次選取所有螢幕"),
            (", / .", "切換到上一次 / 下一次的截圖範圍（尚未加標註時）"),
            ("Esc / 右鍵", "取消目前選區；沒有選區時離開截圖"));
        Paragraph("選取前游標旁會顯示放大鏡，標示目前的螢幕座標與 HEX / RGB 色值，方便精準對齊；亮起的那個是按 C 會複製的格式。");
        Paragraph("要截下拉選單或滑鼠移上去才出現的提示，可以從系統匣選單的「延遲截圖」選擇 3、5 或 10 秒，先把選單打開，倒數結束後就會開始截圖。倒數時右下角會顯示秒數，點一下即可取消。若要把滑鼠游標也截進去，請在「設定」中勾選「截圖時包含滑鼠游標」。");

        Section("標註工具");
        Paragraph("選好範圍後，下方會出現工具列。點選工具後，可以在第二列用滑桿調整粗細（文字與序號為大小），並選擇顏色；每種工具各自記住自己的粗細。滑桿旁的數字可以直接點一下輸入數值，按 Enter 套用、Esc 取消，↑ / ↓ 微調。再點一次同一個工具即可取消。");
        Paragraph("除了 14 種預設顏色，點第二列最右邊的調色盤按鈕可以自訂顏色：在色塊中拖曳選擇飽和度與亮度、右側長條選色相，或直接輸入 HEX 色碼後按 Enter。自訂過的顏色會記在「最近使用」中，下次截圖也能直接選。");
        Shortcuts(
            ("▭  矩形", "拖曳畫出矩形框"),
            ("◯  橢圓", "拖曳畫出橢圓"),
            ("↗  箭頭", "從起點拖曳到箭頭指向的位置"),
            ("╱  直線", "拖曳畫出直線，之後可分別拖曳兩端"),
            ("折線", "逐點點擊放上轉折點，雙擊、Enter 或右鍵結束；點回第一個點會封閉成多邊形。Backspace 刪掉上一個點，Esc 放棄"),
            ("✎  畫筆", "自由手繪"),
            ("螢光筆", "半透明的粗筆，用來標記重點文字"),
            ("對話框", "從要指的位置拖曳到放文字的地方，放開後輸入文字；「文字底色」切換實心或白底外框，拖曳尖端可改變指向"),
            ("①  序號", "點一下放上自動遞增的編號圓點，拖曳可調整位置"),
            ("T  文字", "點一下開始輸入；點選區外、按 Esc 或 Ctrl + Enter 完成。可調整文字大小（8–96）、選字型，並切換粗體、斜體、底線與文字底色"),
            ("▦  馬賽克", "拖曳範圍打上馬賽克或高斯模糊，可切換兩種效果並用「強度」滑桿調整（1–10）"),
            ("放大鏡", "框選要放大的範圍，旁邊會出現放大的畫面；「倍率」調整 2–8 倍，拖曳放大框中央的控制點可移動它"),
            ("聚光燈", "拖曳框出要強調的範圍，其餘部分會調暗，可在第二列調整調暗多少（10%–90%）；可以框好幾個"),
            ("按住 Shift", "矩形畫成正方形、橢圓畫成正圓、箭頭、直線與折線鎖定 45° 方向，畫筆與螢光筆畫直線"),
            ("填滿 / 虛線", "矩形與橢圓可以填滿顏色；矩形、橢圓、箭頭、直線、畫筆可以改畫虛線"),
            ("不透明度", "第二列的滑桿調整標註的不透明度（10%–100%），例如畫半透明的色塊"),
            ("吸管", "點第二列的吸管按鈕後，在截圖上點一下取色當畫筆顏色（Esc 取消）"),
            ("Ctrl + Z", "復原"),
            ("Ctrl + Y", "重做"));

        Paragraph("畫好的標註在截圖完成前都可以再調整，剛畫完的標註會自動被選取：");
        Shortcuts(
            ("點擊標註", "選取（矩形、橢圓、箭頭、畫筆請點在線條上；填滿的形狀點內部也可以）"),
            ("拖曳標註", "移動"),
            ("拖曳控制點", "調整大小；箭頭與直線可分別拖曳兩端，折線可拖曳每個轉折點"),
            ("調整顏色 / 粗細 / 樣式", "套用到選取的標註（含填滿、虛線、不透明度、馬賽克強度）"),
            ("方向鍵", "選取的標註移動 1 像素"),
            ("雙擊文字", "重新編輯文字內容"),
            ("Ctrl + D", "複製一份選取的標註"),
            ("Ctrl + Shift + C / Ctrl + V", "複製標註，再貼到游標位置；也能貼到下一張截圖"),
            ("Ctrl + ] / Ctrl + [", "上移 / 下移一層；加 Shift 移到最上 / 最下層"),
            ("右鍵點標註", "開啟選單：複製、貼上、調整上下層、刪除"),
            ("Delete", "刪除選取的標註"),
            ("Esc / 右鍵空白處", "取消選取標註"));

        Paragraph("截圖完成後，按 Ctrl + Shift + F1 或從系統匣選單選「再次開啟上一張截圖」，會回到上一次的畫面與選取範圍，標註也都還能移動、修改或刪除，適合補上漏掉的標註後重新複製或存檔。");

        Section("輸出");
        Shortcuts(
            ("Enter / 雙擊選區", "預設動作：複製到剪貼簿；可在「設定」中改成快速存檔、另存新檔或貼到桌面"),
            ("Ctrl + C", "複製到剪貼簿"),
            ("Ctrl + S", "另存為 PNG / JPG / BMP"),
            ("Ctrl + Shift + S", "快速存檔：不跳對話框，直接存成 PNG 到設定的資料夾"),
            ("Ctrl + T", "貼到桌面（貼圖）"),
            ("文字辨識按鈕", "辨識選取範圍內的文字，可修改後複製"),
            ("長截圖按鈕", "自動往下捲動並拼接成一張長圖"));
        Paragraph("這些功能也可以從工具列右側的按鈕使用。標註會一起合成到輸出的圖片中。");
        Paragraph("存檔的預設檔名可以在「設定」的「檔名格式」中自訂：{date} 日期、{time} 時間、{title} 截圖的視窗標題、{n} 流水號（001、002…），也可以用 {yyyy-MM-dd} 這類日期格式。例如「{title}_{n}」會存成「記事本_001.png」。設定中也可以勾選存檔後複製檔案路徑，或在檔案總管中顯示存好的檔案。");

        Section("貼圖");
        Paragraph("貼圖會固定在所有視窗的最上層，適合拿來對照資料或暫存畫面。截圖時按 Ctrl + T，或按 F3 貼上剪貼簿中的圖片。剪貼簿中是文字時，F3 會把文字貼成一張便條；是色碼（例如 #1E88E5 或 rgb(30, 136, 229)）時則貼成該顏色的色塊。");
        Shortcuts(
            ("拖曳", "移動貼圖"),
            ("滾輪", "縮放"),
            ("Ctrl + 滾輪", "調整透明度"),
            ("Ctrl + 0", "還原原始大小與透明度"),
            ("Space", "編輯標註：回到截圖編輯器，在貼圖上加標註"),
            ("1 / 2", "向左 / 向右旋轉 90°"),
            ("3 / 4", "水平 / 垂直翻轉"),
            ("O", "文字辨識：辨識貼圖中的文字"),
            ("X", "滑鼠穿透：點擊會穿過貼圖，可從系統匣選單解除"),
            ("Ctrl + C", "複製貼圖內容"),
            ("Ctrl + S", "另存貼圖"),
            ("Ctrl + Shift + S", "快速存檔"),
            ("雙擊 / Esc", "關閉貼圖"),
            ("右鍵", "開啟選單"));
        Paragraph("系統匣選單可以一次隱藏或顯示所有貼圖，也能還原最近關閉的貼圖（最多 20 張，會回到原本的位置、大小與透明度）。");

        Section("錄製螢幕");
        Paragraph("可以把螢幕上的任一區域錄成 GIF 動畫或 MP4 影片，滑鼠游標也會一起錄進去。");
        Shortcuts(
            ("Shift + F1", "框選要錄製的範圍（與截圖相同，可自動偵測視窗）"),
            ("GIF / MP4", "選擇輸出格式，會記住上次的選擇"),
            ("Enter / 雙擊選區", "開始錄製"),
            ("錄影按鈕", "截圖時選好範圍後，點工具列的錄影按鈕也能開始錄製"),
            ("暫停 / 繼續", "暫時停止錄製，暫停的時間不會出現在影片中；也可以從系統匣選單操作"),
            ("Shift + F1 / 停止", "停止錄製並選擇儲存位置"),
            ("取消", "停止並捨棄這次錄製"));
        Paragraph("在「設定」中勾選「錄影時標示滑鼠點擊」後，按下滑鼠時游標周圍會出現圓圈（左鍵橘色、右鍵藍色）；勾選「錄影時顯示按下的按鍵」後，畫面下方會顯示剛按下的按鍵組合（例如 Ctrl + C），適合錄操作教學。注意按鍵顯示也會錄下輸入的文字，錄製時請避免輸入密碼。");
        Paragraph("錄製中，範圍外會顯示紅框與計時列，這些都不會被錄進去；計時列擋到畫面時可以拖曳移開。儲存後檔案會同時複製到剪貼簿，可直接貼到聊天軟體或資料夾。");
        Bullets(
            "GIF：每秒 10 格，到處都能直接播放，適合短的操作示範；畫面沒有變化時檔案幾乎不會變大",
            "MP4：每秒 30 格的 H.264 影片，動作流暢、檔案小，適合較長或畫面變化大的錄製");

        Section("系統匣");
        Paragraph("左鍵點系統匣圖示可直接截圖；右鍵開啟選單：");
        Bullets(
            "截圖、延遲截圖（3 / 5 / 10 秒）、再次開啟上一張截圖、錄製螢幕（錄製中顯示為「停止錄製」與「暫停錄製」）",
            "剪貼簿貼為貼圖（圖片、文字或色碼）、隱藏 / 顯示所有貼圖、還原上次關閉的貼圖、關閉所有貼圖",
            "解除所有貼圖的滑鼠穿透",
            "開機時啟動：勾選後登入 Windows 時會自動執行 ScreenCap",
            "設定：更改熱鍵、預設存檔資料夾、檔名格式、存檔後的動作、Enter 的預設動作、外觀（淺色 / 深色 / 跟隨 Windows）、是否截進滑鼠游標、錄影時是否標示點擊與顯示按鍵",
            "使用說明：開啟本頁",
            "結束：關閉 ScreenCap（所有貼圖也會一併關閉）");

        Section("長截圖");
        Paragraph("適合截取超過一個畫面的網頁、文件或聊天紀錄。框選要捲動的內容區域（例如網頁的內文），點工具列的長截圖按鈕，ScreenCap 會把游標移到區域中央，用滑鼠滾輪自動往下捲，並把每次新出現的部分接到底下。");
        Bullets(
            "捲到底、按狀態列的「完成」或達到 30,000 像素時停止；按「取消」則捨棄",
            "網頁頂端或底部固定不動的區塊（例如固定的頁首）只會保留一份",
            "選取範圍內有少量不跟著捲動的部分（例如捲軸、側邊欄）也能正確對齊",
            "完成後可在預覽視窗中複製、另存或做文字辨識",
            "捲動過程中請不要移動滑鼠或操作視窗；若內容在捲動時改變（動畫、影片、自動載入），可能會提早停止");

        Section("文字辨識");
        Paragraph("截圖時點工具列的文字辨識按鈕，或在貼圖上按 O，會開啟辨識結果視窗。辨識使用 Windows 內建的 OCR，不需要連網；結果可以直接修改，按「複製」或 Ctrl + Enter 複製並關閉。中英文混合時建議選「中文」，可在視窗上方切換辨識語言。");

        Section("常見問題");
        Question("錄出來的 GIF 檔案很大？",
            "GIF 只會記錄每格有變化的部分。錄製影片、捲動網頁等整個畫面都在變動的內容時檔案會比較大，可以改用 MP4 或縮小錄製範圍。");
        Question("文字辨識的語言清單是空的，或辨識不出中文？",
            "辨識語言來自 Windows 已安裝的語言套件。到「設定 → 時間與語言 → 語言與地區」新增語言（例如中文（台灣）），並確認有安裝「光學字元辨識」功能。");
        Question("按熱鍵沒有反應？",
            "熱鍵可能被其他程式占用，啟動時系統匣會跳出提示。可以在「設定」中改用其他組合，或從系統匣圖示截圖。");
        Question("開機時啟動沒有作用？",
            "設定記錄的是勾選當下執行檔的位置。如果之後搬移或刪除了 ScreenCap，請從新位置執行後重新勾選。");
        Question("同時開了兩個 ScreenCap？",
            "ScreenCap 同一時間只會執行一份，重複開啟時新的會直接結束。");
    }

    // ---------------------------------------------------------------- builders

    private void Heading(string text, double size, double top) =>
        _body.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, top, 0, 8),
        });

    private void Section(string title)
    {
        Heading(title, 18, top: 20);
        _body.Children.Add(new Border { Height = 3, Width = 32, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, -4, 0, 10) }
            .Themed(Border.BackgroundProperty, "Brush.Accent"));
    }

    private void Paragraph(string text) =>
        _body.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
            Margin = new Thickness(0, 0, 0, 10),
        }.Themed(TextBlock.ForegroundProperty, "Brush.Muted"));

    private void Bullets(params string[] items)
    {
        foreach (var item in items)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 0, 6) }.Themed(TextBlock.ForegroundProperty, "Brush.Muted");
            var bullet = new Run("•  ");
            bullet.SetResourceReference(TextElement.ForegroundProperty, "Brush.Accent");
            text.Inlines.Add(bullet);
            text.Inlines.Add(new Run(item));
            _body.Children.Add(text);
        }
    }

    private void Question(string question, string answer)
    {
        _body.Children.Add(new TextBlock { Text = question, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) });
        Paragraph(answer);
    }

    private void Shortcuts(params (string Key, string Description)[] rows)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = new Thickness(0, 0, 0, i == rows.Length - 1 ? 0 : 1);

            var key = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 3),
                Margin = new Thickness(0, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = rows[i].Key, FontSize = 13 },
            }.Themed(Border.BackgroundProperty, "Brush.Card");
            var keyCell = new Border { BorderThickness = line, Child = key }.Themed(Border.BorderBrushProperty, "Brush.Border");
            var description = new Border
            {
                BorderThickness = line,
                Child = new TextBlock { Text = rows[i].Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) },
            }.Themed(Border.BorderBrushProperty, "Brush.Border");

            Grid.SetRow(keyCell, i);
            Grid.SetRow(description, i);
            Grid.SetColumn(description, 1);
            grid.Children.Add(keyCell);
            grid.Children.Add(description);
        }
        _body.Children.Add(grid);
    }
}
