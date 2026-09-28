namespace SidebarExplorer.App.Services;

// What F1 shows: the app as a list of things you can do and how to do them.
//
// ROWS ONLY, no prose (2026-08-11). Every section used to open with a sentence
// and close with a note, and both went: nobody reads them, the register never
// quite matched the words people actually use, and a page of paragraphs made a
// simple app look hard to learn. Whatever in those notes was worth keeping
// became a ROW, which is the shape that gets read.
//
// AND A ROW HAS TO EARN ITS LINE. If the menu item's own name already says what
// it does - 우클릭 → 북마크 doing "keep it in the side panel" - the row is
// reading the label back and it goes. What belongs here is what the label
// cannot say: keys, gestures, and the consequences nobody would guess.
//
// BOTH LANGUAGES LIVE ON THE SAME LINE. Two files would drift the first time a
// row was added in a hurry, and the drift would be invisible until someone
// switched language; here a half-written row does not compile.
//
// This is a document about behaviour, so it goes stale when behaviour changes
// and nothing here can notice. The keys are declared inline in MainWindow's key
// handler, so there is no table to read them off - which means the release
// checklist has to carry "read the help through" until there is.
public static class HelpContent
{
    public sealed record Row(string Gesture, string Meaning);

    // SubTitle starts a group inside a section; empty means the rows follow the
    // section title directly.
    public sealed record Group(string SubTitle, IReadOnlyList<Row> Rows);

    public sealed record Section(string Title, IReadOnlyList<Group> Groups);

    private static Row R(string koGesture, string koMeaning, string enGesture, string enMeaning)
        => Strings.IsEnglish ? new Row(enGesture, enMeaning) : new Row(koGesture, koMeaning);

    private static string T(string ko, string en) => Strings.IsEnglish ? en : ko;

    // The box at the top. The one place here that is not a gesture and its
    // meaning, and it earns that: everything below answers "how do I do X",
    // which only helps once you know what X is worth doing.
    public static string TipsTitle
        => T("처음 사용자 TIP", "New here? Start with these");

    public static IReadOnlyList<string> Tips() => new[]
    {
        T("고정/자동 숨김 선택 및 크기(높이) 조정", "Pin it or let it auto-hide, and set the size"),
        T("안 쓰는 폴더 감추기", "Exclude the folders you never open"),
        // Which of the two wins is the part nobody guesses, and it is the part
        // that makes the pair usable at all.
        T("자주 쓰는 파일 형식(확장자) 지정하기 (제외 확장자 입력시 최우선 적용)",
          "Set the file kinds you work with - anything you type as excluded wins over the rest"),
        T("자주 가는 곳은 북마크로", "Bookmark the places you keep going back to"),
        // 요청 2026-08-19. 두 동작이 한 줄에 서 있고, 부르는 조건이 서로
        // 다르다는 것이 이 줄을 짧게 쓰기 어려운 이유다: 소리는 패널을 닫아야
        // 나오고(배경 재생이 켜져 있을 때), 보다 만 영상은 트리가 그 폴더를
        // 떠나면 나오며 패널이 열려 있어도 보인다. 쓰는 사람에게는 한 가지
        // 이야기 - "듣던 것·보던 것이 트리 아래에 남는다" - 이므로 한 줄로 두고,
        // 조건 중에서는 소리 쪽만 적었다. 그것이 안 하면 안 나오는 쪽이다.
        // 두 낱말의 시제가 다른 것이 일부러다(사용자, 2026-08-19). 소리는 실제로
        // 계속 돌고 있고 영상은 멈춰 세워 둔 것이라, 둘 다 과거형으로 뭉치면
        // 음악도 멈춘 것처럼 읽힌다. 두 글자로 살 수 있는 구분이다.
        T("재생 중인 음악·보던 영상은 트리 하단 줄에서 바로 복귀 (음악은 패널을 닫아도 계속 재생)",
          "The row at the foot of the tree takes you back to the music still playing, or the film you were watching - and music carries on with the panel closed"),
        T("썸네일 캐싱 활성화로 이미지 많은 폴더 빠르게 관리",
          "Turn thumbnail caching on and move through big image folders fast"),
        T("원하는 색상 선택", "Pick the colors you want"),
        T("설정한 상태를 프리셋으로 저장 (제목 표시줄 우클릭 메뉴)",
          "Save a setup as a preset (right-click the title bar)"),
    };

    public static IReadOnlyList<Section> Build() => new[]
    {
        new Section(
            // "사이드바"였다가 2026-08-16에 바꿈 - 공식 명칭이 아님. 바로 다음
            // 절이 창 모드이므로, 이 절은 그 반대편(도킹된 기본 모습)이 아니라
            // 앱 자체를 다루는 자리로 읽히는 것이 맞음 - 트레이·핀·너비·높이가
            // 다 여기 있음.
            T("앱", "The app"),
            new[]
            {
                new Group(string.Empty, new[]
                {
                    R("가장자리 혹은 손잡이에 커서 이동", "다시 펼치기",
                      "Point at the screen edge, or at the handle", "Bring it back"),
                    // THE ROW SOMEONE LOOKS UP IN A PANIC, added 2026-08-14
                    // after a report of exactly that: two monitors, the sidebar
                    // auto-hidden onto the edge they share, and a handle a
                    // cursor crosses to the next display instead of resting on.
                    // The help had one way back and it was the one that had
                    // stopped working for them. Both of these open the sidebar
                    // fully, not to the handle, which is why the row says 펼치기
                    // rather than 표시.
                    R("트레이 아이콘 클릭 · 프로그램 다시 실행", "숨은 앱 다시 펼치기",
                      "Click the tray icon, or run the app again", "Opens a hidden app back up"),
                    R("핀 클릭", "고정하기 ↔ 자동 숨김",
                      "Click the pin", "Stay open ↔ auto-hide"),
                    // Three answers under one button, so the row is split rather
                    // than made to carry an if-clause: what it does depends on
                    // the tray option AND on which mode the window is in, and a
                    // single sentence covering both read as a paragraph.
                    R("제목 표시줄 최소화 아이콘", "트레이 옵션과 모드에 따라 다름",
                      "The minimize icon in the title bar", "Depends on the tray option and the mode"),
                    // Indented AND bulleted: the indent alone left them reading
                    // as three ordinary rows that happened to start further in,
                    // and the dash is what says they belong to the row above.
                    R("  - 트레이 옵션 켜짐", "트레이로 최소화",
                      "  - Tray option on", "Minimize to the tray"),
                    // NOT 앱 here, unlike the rows around it: this half of the
                    // row is a CONDITION, and the condition is which mode the
                    // window is in - the English side has said 도킹 all along
                    // and only the Korean was calling it 사이드바.
                    R("  - 트레이 꺼짐 · 도킹 상태", "숨김",
                      "  - Tray off, docked", "Hide it"),
                    R("  - 트레이 꺼짐 · 창 모드", "작업 표시줄로 최소화",
                      "  - Tray off, window mode", "Minimize to the taskbar"),
                    R("제목 표시줄 우클릭", "도움말 · 트레이로 최소화 · 다시 시작 · 종료",
                      "Right-click the title bar", "Help · Minimize to tray · Restart · Quit"),
                    R("가장자리 드래그", "너비 조절",
                      "Drag the outer edge", "Resize"),
                    R("가장자리 더블클릭", "내용에 맞춰 너비 맞춤",
                      "Double-click the outer edge", "Fit the width to the contents"),
                    R("위 · 아래 가장자리 드래그", "높이와 위치 (짧은 밴드)",
                      "Drag the top or bottom edge", "Height, and where the band sits"),
                    R("그 가장자리 더블클릭", "화면 높이 전체로 복원",
                      "Double-click that edge", "Back to the full height"),
                    R("드래그 중 Shift · Ctrl", "격자에 맞춰 스냅 (Ctrl이 더 작은 격자)",
                      "Shift · Ctrl while dragging", "Snap to a grid - Ctrl uses a smaller one"),
                }),
            }),

        new Section(
            T("창 모드", "Window mode"),
            new[]
            {
                new Group(string.Empty, new[]
                {
                    R("제목 표시줄 드래그", "창으로 분리 · 이동",
                      "Drag the title bar", "Pull it off, and move it"),
                    R("제목 표시줄 더블클릭", "최대화 · 복원",
                      "Double-click the title bar", "Maximize · restore"),
                    R("가장자리 · 모서리 드래그", "크기 조절",
                      "Drag an edge or corner", "Resize"),
                    R("가장자리로 드래그", "다시 도킹",
                      "Drag to a screen edge", "Dock it again"),
                    R("핀 클릭", "도킹 상태로 복귀",
                      "Click the pin", "Go back to being docked"),
                }),
            }),

        // A SECTION OF ITS OWN since 2026-08-16, having been a group inside
        // 창 모드 - put there because what a preset holds is mostly the window
        // and one group seemed enough for the colours and the filter riding
        // along.
        //
        // It has outgrown that twice over. What a preset holds now reaches the
        // whole palette, the panel's own switches and the folder it was saved
        // in, none of which is a window mode; and it has keys, which is what
        // a reader scans these headings for. A subject nested under another
        // one is a subject a reader has to already know the location of.
        new Section(
            T("프리셋", "Presets"),
            new[]
            {
                new Group(string.Empty, new[]
                {
                    R("제목 표시줄 우클릭", "저장된 프리셋 목록 · 프리셋 추가 (최대 5개)",
                      "Right-click the title bar", "The setups you saved, and Add preset - up to five"),
                    // The list this row used to carry (위치 · 크기 · 도킹 · 자동
                    // 숨김 · 색상 · 파일 종류 · 현재 폴더) came out on the author's
                    // call (2026-08-16): seven items read as seven things to
                    // check, when the answer a reader wants is that they do not
                    // have to check any of them. It also went stale twice as
                    // AppPreset.Fields grew, which a sentence cannot.
                    R("저장 항목", "현재 앱 설정 전체를 그대로 저장",
                      "What it holds", "Everything you have set up, exactly as it stands"),
                    R("이름 옆 ›", "적용 · 덮어쓰기 · 이름 바꾸기 · 삭제",
                      "The › beside a name", "Apply · overwrite · rename · delete"),
                    // The two keys, which is what this section is for - the rows
                    // above name a menu, and these are the reason not to open it.
                    R("Ctrl+1 ~ 5", "해당 번호의 프리셋으로 전환",
                      "Ctrl+1 ~ 5",
                      "Go to that preset"),
                    R("Ctrl+Shift+S", "현재 선택된 프리셋에 확인 없이 바로 덮어씌움",
                      "Ctrl+Shift+S",
                      "Overwrites the preset you are in, straight away, without asking"),
                }),
            }),

        new Section(
            T("트리", "The tree"),
            new[]
            {
                new Group(string.Empty, new[]
                {
                    R("↑ ↓", "위 · 아래 행 이동", "↑ ↓", "Previous · next row"),
                    // Straight under ↑ ↓ because it is the same axis with the
                    // files taken out, and the two are chosen between rather
                    // than used apart. Both conditions are in the row: it walks
                    // what is ON SCREEN and expands nothing, so someone who
                    // expects it to dive into a closed folder reads the first
                    // press as a miss (missing from here until 2026-08-19).
                    R("PageUp · PageDown", "화면에 나와 있는 폴더로만 위 · 아래 이동",
                      "PageUp · PageDown",
                      "Up · down through the folders on screen, skipping the files - nothing gets expanded"),
                    R("← →", "접기 · 펼치기", "← →", "Collapse · expand"),
                    // 2026-08-17: 열린 폴더를 눌러도 안 접히게 바뀌었으므로 이
                    // 두 줄이 필요해졌다. 접는 방법이 둘(다시 누르기 · 펼침기호)이고
                    // 어느 쪽도 화면에 안 적혀 있다.
                    R("폴더 클릭", "접힌 폴더는 펼치기 · 열린 폴더는 선택만, 다시 클릭하면 접기",
                      "Clicking a folder",
                      "A closed folder opens; an open one is only selected - press it again to close"),
                    // 2026-08-29: 위 줄이 설정에 따라 절반만 맞게 되었으므로 조건
                    // 줄을 붙인다. 위 줄을 고쳐 두 상태를 한 문장에 담는 대신
                    // 줄을 나눈 것은 바로 아래 `전체 접기 아이콘 Shift+클릭` 이
                    // 이미 같은 짜임이기 때문 - 왼쪽 칸이 조건을 부르고 오른쪽이
                    // 그때의 동작만 말한다.
                    R("클릭 시 바로 펼침을 끄면", "첫 클릭은 선택 · 두 번째 클릭이 펼치기 · 접기",
                      "With Expand on a single click off",
                      "The first click selects; the second one opens or closes"),
                    R("펼침기호 클릭", "설정과 무관하게 누를 때마다 바로 접기 · 펼치기",
                      "Clicking the expand arrow",
                      "Opens and closes on every press, whatever that setting says"),
                    // 두 줄인 이유는 버튼이 두 가지 일을 하기 때문이 아니라,
                    // 조합 쪽이 되돌릴 수 없기 때문이다. 툴팁이 말하고는 있지만
                    // 툴팁은 올려 놓아야 보이고, 복원이 안 되는 쪽은 누르기 전에
                    // 알아야 하는 종류의 것이다.
                    R("제목 표시줄의 전체 접기 아이콘", "펼친 폴더를 전체 접음, 다시 클릭하면 펼침 상태 복원",
                      "The Collapse All icon in the title bar",
                      "Folds every open folder; press it again to put them back"),
                    R("전체 접기 아이콘 Shift+클릭", "펼침 상태를 완전히 접음 (복원 안 됨)",
                      "Shift-clicking the Collapse All icon",
                      "Folds for good (no restore)"),
                    // 바로 위 줄과 같은 동작이라 그 아래 붙는다. 키가 따로 있는
                    // 이유는 손이 트리에 있을 때 제목 표시줄까지 가지 않으려는
                    // 것이므로, 설명은 되풀이하지 않고 같은 것이라고만 말한다.
                    R("Shift+Backspace", "위와 같음 (복원 안 됨)",
                      "Shift+Backspace", "The same, from the keyboard (no restore)"),
                    // Named for the places rather than for the rows: it moves
                    // between folders you have been in, not between every row
                    // you clicked, and someone who expects the second one will
                    // read the first press as a miss.
                    R("Ctrl+← · Ctrl+→", "사용한 폴더로 뒤로 · 앞으로 (경로 표시줄의 < > 와 같음)",
                      "Ctrl+← · Ctrl+→",
                      "Back · forward through the folders you have used - the same as the path bar's < >"),
                    // Under the keys that do the same thing, because that is
                    // what they do - not a separate feature, a second way in.
                    // Reported missing 2026-08-19: the buttons had a row for
                    // the film and none for the two things they were given on
                    // the same day.
                    R("마우스 앞 · 뒤 버튼", "트리 위에서 클릭하면 사용한 폴더로 뒤로 · 앞으로",
                      "Mouse back · forward buttons",
                      "Over the tree, back · forward through the folders you have used"),
                    R("경로 표시줄의 목록 버튼", "사용한 폴더들을 확인하고 바로 이동",
                      "The list button on the path bar",
                      "See the folders you have used, and go straight to one"),
                    R("Ctrl + 휠", "빠른 스크롤", "Ctrl + wheel", "Scroll about five times faster"),
                    R("Ctrl + / −", "글자 크기 조절", "Ctrl + / −", "Text size"),
                    R("Enter", "열기", "Enter", "Open"),
                    R("F2", "이름 바꾸기", "F2", "Rename"),
                    R("F5", "새로고침", "F5", "Refresh"),
                    R("F7", "새 폴더", "F7", "New folder"),
                    R("Del", "휴지통으로 삭제", "Del", "Delete to the Recycle Bin"),
                    R("Shift+Del", "완전 삭제", "Shift+Del", "Delete permanently"),
                    R("Ctrl+A", "같은 폴더의 항목 전체 선택", "Ctrl+A", "Select all in the same folder"),
                    R("Ctrl+C · Ctrl+X · Ctrl+V", "복사 · 잘라내기 · 붙여넣기",
                      "Ctrl+C · Ctrl+X · Ctrl+V", "Copy · cut · paste"),
                    R("Ctrl+Shift+C", "경로 복사", "Ctrl+Shift+C", "Copy path"),
                    R("Alt+Enter", "속성", "Alt+Enter", "Properties"),
                    // Said as COPY since 2026-08-13. It always was one - the row
                    // had claimed 이동 since the drag shipped, which is the kind
                    // of line that sends someone looking for a feature that is
                    // there and a behaviour that is not.
                    R("드래그", "같은 드라이브는 이동, 다른 드라이브는 복사",
                      "Drag", "Move within a drive, copy between drives"),
                    R("다른 탐색기로 드래그", "복사", "Drag to another explorer", "Copy"),
                    // The KEY column carries the timing, because that is the
                    // part that gets missed: Shift+클릭 is already 범위 선택, so
                    // holding it first selects instead of dragging.
                    R("드래그 중 Shift · Ctrl", "이동 · 복사 고정",
                      "Shift · Ctrl while dragging", "Force a move · force a copy"),
                    R("Ctrl+Alt+K", "북마크 표시 · 해제", "Ctrl+Alt+K", "Bookmark, or take it back"),
                    R("Ctrl+Alt+L · J", "다음 · 이전 북마크로 이동", "Ctrl+Alt+L · J", "Next · previous bookmark"),
                    // 번호가 화면에 있으니 "순서가 바뀐다"는 라벨이 말해 준다.
                    // 여기 적을 값어치가 있는 것은 그 순서가 단축키의 순서이기도
                    // 하다는 것 - 목록만 보면 알 길이 없다.
                    R("북마크 패널에서 행 드래그", "순서 변경 · 번호가 곧 Ctrl+Alt+L 순서",
                      "Drag a row in the bookmark panel",
                      "Reorders it - the numbers are the Ctrl+Alt+L order"),
                    // The parenthesis is the part a menu label cannot say, and
                    // it is what a whole paragraph used to say instead.
                    R("우클릭 → 숨기기", "트리에서 폴더나 드라이브를 감춤, 검색에서는 보이나 작업 시 숨김 해제 필요",
                      "Right-click → Exclude",
                      "Takes a folder or a drive out of the tree; search still finds it, but working on it means putting it back first"),
                    // THE MENU WITH NO ROW UNDER IT. Everything else in this
                    // section acts on the row that was clicked, so a menu that
                    // needs the absence of one is the gesture nobody arrives at
                    // by accident - and the four lists behind it are reachable
                    // no other way without opening the options menu.
                    //
                    // Listed rather than summarised, because the list IS the
                    // reason to go there. 새로고침 is called 전체 here for the
                    // one thing this menu's version does that the row's does
                    // not: the row refreshes that folder, this one the drive
                    // list and every folder open in the tree.
                    R("빈 곳 우클릭",
                      "전체 새로고침 · 새 폴더 · 북마크 · 숨긴 폴더 · 네트워크 위치 · 표시할 파일 형식",
                      "Right-click empty space",
                      "Refresh everything · New folder · Bookmark · Excluded folders · Network Locations · File types"),
                }),
            }),

        new Section(
            T("필터와 정렬", "Filtering and sorting"),
            new[]
            {
                new Group(string.Empty, new[]
                {
                    R("아래쪽 필터 버튼 줄", "종류별 필터링",
                      "The filter buttons at the bottom", "Show only some kinds of file"),
                    // 동작이 바뀐 것을 적는 줄 (2026-08-17). 필터를 변경하면 트리가
                    // 접히고 맨 위로 가는데, 그것을 알리는 곳이 어디에도 없었다.
                    //
                    // 처음에는 "보던 위치는 북마크로 복귀"를 괄호로 붙였고 사용자가
                    // 바로 뺐다. 되돌리는 방법을 같은 줄에 적는 것은 **잃은 것에
                    // 대한 변명으로 읽힌다** - 도움말은 동작을 적는 자리이고, 사용자를
                    // 달래는 자리가 아니다. 사실만 적는다.
                    R("필터 변경 시", "트리를 접고 맨 위로 이동",
                      "When a filter changes", "The tree folds and goes to the top"),
                    R("필터 버튼 → 사용자 지정", "확장자 직접 입력 ( *. 없이 쉼표로 구분해서 여러 개 입력)",
                      "A filter button → Custom",
                      "Type the extensions yourself - no *. , and comma-separated for several"),
                    // 빈 폴더로 오해하고 지우는 것을 막는 줄이므로 필터 쪽에 둔다 -
                    // 이 줄을 보는 사람은 필터를 켜 둔 사람이다.
                    R("필터가 전부 감춘 폴더", "감춰진 파일 개수를 폴더 안에 표시",
                      "A folder the filter empties",
                      "Shows how many files it is hiding, in the folder's own place"),
                    // The same list twice, and it earns the second line: this
                    // section is where someone hunting the filter looks, and
                    // they will not scroll up to 트리 to find out that the
                    // gesture exists. The row there answers "what is that menu";
                    // this one answers "where else can I set this".
                    R("트리 빈 곳 우클릭 → 표시할 파일 형식", "필터 버튼 줄과 같은 목록",
                      "Right-click empty tree space → File types",
                      "The same list the filter buttons carry"),
                    R("폴더 행의 정렬 아이콘", "그 폴더의 개별 정렬, 하위 폴더는 상위 폴더의 정렬을 따름(단, 하위 폴더를 개별 정렬할 경우 별개로 유지)",
                      "The sort icon on a folder row",
                      "Sort that folder its own way - subfolders follow, unless they have their own"),
                    // 위 줄은 결과를 적고 이 줄은 그 결과를 만드는 항목을 적는다.
                    // 이름이 화면에 있는데 도움말에 없으면, 메뉴를 열어 본 사람이
                    // 무엇을 활성화하고 비활성화하는 것인지 알 길이 없다.
                    R("정렬 메뉴 → 부모 폴더 따르기",
                      "기본 활성화, 개별 정렬을 선택하면 해제됨 (다시 선택하면 그 폴더의 개별 지정이 삭제됨)",
                      "Sort menu → Follow the parent folder",
                      "On by default; picking a field of its own clears it, and choosing it again drops that folder's own setting"),
                    R("옵션 → 정렬 기준", "전체 기본 정렬", "Options → Sort by", "The default for everything"),
                    // 접기 belongs on the same row as 더 보기, and the selection
                    // is the point of the row rather than a side effect: a long
                    // list puts the 더 보기 row at the bottom of the screen with
                    // its folder scrolled off, so two identically named folders
                    // are told apart by which guide line lights up.
                    R("더 보기 · 접기", "나머지 항목 펼치기 · 되접기 (그 부모 폴더가 선택됨)",
                      "Show more · Show fewer", "Reveal the rest, or fold it back - its parent folder gets selected"),
                }),
            }),

        new Section(
            T("검색", "Search"),
            new[]
            {
                new Group(string.Empty, new[]
                {
                    R("Ctrl+F", "검색 열기", "Ctrl+F", "Open search"),
                    R("Ctrl+Shift+F", "선택한 폴더에서 검색 (파일은 그 파일이 있는 폴더)",
                      "Ctrl+Shift+F", "Search the selected folder (for a file, the folder it is in)"),
                    R("제목 표시줄 돋보기", "검색 열기 · 닫기",
                      "The magnifier in the title bar", "Open · close search"),
                    R("폴더 아이콘", "검색 범위 선택", "The folder icon", "Choose where to look"),
                    // The five rows are two questions since 2026-08-31, and that
                    // is the half a reader cannot get from the icon: the grouping
                    // is a toggle that stays on while the sort changes under it.
                    R("검색창의 정렬 아이콘", "폴더별 묶기 · 이름 · 수정한 날짜 · 오름차순 · 내림차순, 묶기와 정렬은 각각 적용",
                      "The sort icon in the search bar",
                      "Group by folder · Name · Date modified · Ascending · Descending - the grouping and the sort apply separately"),
                    // Three rows for what is one conditional behaviour, and the
                    // condition is what each row turns on: with the panel shut
                    // a click can only mean "take me there", and with it open
                    // that meaning moves to the double click so the single one
                    // can show the file without collapsing the list. Stated as
                    // three plain outcomes rather than as one row with an
                    // if-clause in it.
                    R("결과 클릭", "트리의 해당 항목으로 이동",
                      "Click a result", "Go to it in the tree"),
                    R("결과 클릭 · ↑ ↓ (멀티미디어 패널 열림)", "검색 결과 간 이동",
                      "Click a result · ↑ ↓", "(with the multimedia panel open) Move through the results"),
                    R("결과 더블클릭 (멀티미디어 패널 열림)", "트리의 해당 항목으로 이동",
                      "Double-click a result", "(with the multimedia panel open) Go to it in the tree"),
                    R("결과 표시 수", "기본 1,000개 · 목록 끝 더 보기로 1,000개씩 추가",
                      "How many results show", "1,000 at a time - \"Show more\" at the end adds another 1,000"),
                    // The dot is the half a label cannot say: the button names
                    // the act, nothing names the reason to press it.
                    R("상태줄 오른쪽 ↻", "다시 인덱싱, 변경된 내용이 있을 경우 파란 점 표시",
                      "The ↻ on the status line", "Reindex - a blue dot means the folder has changed"),
                    R("Esc · Ctrl+E", "검색 닫기", "Esc · Ctrl+E", "Close search"),
                }),
            }),

        new Section(
            // "이미지 뷰어" until 2026-08-11, when the panel stopped being one:
            // it plays film and sound now, and two of the four groups under
            // this heading are about things that are not images. This heading
            // went first and the rest of the app followed on 2026-08-12 - the
            // panel is called 멀티미디어 패널 everywhere now, so the heading names
            // it outright rather than naming the subject it covers.
            T("멀티미디어 패널", "Multimedia Panel"),
            new[]
            {
                // HOW THE PANEL OPENS AT ALL, which this section did not say
                // (2026-08-16). Every group under it described what to do once
                // it is open, and 검색 has had the equivalent row - 제목 표시줄
                // 돋보기 - since it shipped. The two options below it are here
                // rather than in 모양과 설정 because they are ways IN, and
                // someone reading this heading is looking for exactly that.
                new Group(string.Empty, new[]
                {
                    R("제목 표시줄 이미지 아이콘", "멀티미디어 패널 열기 · 닫기",
                      "The picture icon in the title bar", "Open · close the multimedia panel"),
                    R("옵션 → 멀티미디어 패널 → 더블클릭으로 열기",
                      "트리에서 더블클릭한 파일을 기본 프로그램 대신 패널에서 엶",
                      "Options → Multimedia panel → Open on double-click",
                      "A double-click in the tree opens the file here instead of in your usual program"),
                    // The condition is the row, and it is the author's own
                    // wording: what it does for a folder is nothing, which
                    // reads as the option being broken unless it is said.
                    R("옵션 → 멀티미디어 패널 → 자동 펼치기",
                      "미디어 파일을 선택할 경우에만 패널이 펼쳐짐 (폴더는 동작하지 않음)",
                      "Options → Multimedia panel → Expand on selection",
                      "Expands the panel when a media file is selected - folders do nothing"),
                    // The cover takes the header away, so this says where its
                    // menu went. Its companion - the line that names the file
                    // at the foot on a mouse move - had a row here too and it
                    // came out (2026-08-16): the line shows ITSELF the moment
                    // the cover goes up, so a row telling someone to move the
                    // mouse explains a thing that has already explained itself.
                    // Its gesture also ran long enough to wrap the column.
                    //
                    // What the row lists came back once (2026-08-16). Folding
                    // away everything but the show left a picture with no way
                    // to reach its own size, navigator or thumbnail bar, since
                    // the chip row those live on went up with the header. Only
                    // the FILE items fold now, so the row names both halves.
                    R("전체 화면에서 우클릭", "파일 항목 대신 그림 크기 · 내비게이터 · 썸네일 바와 제목 표시줄 메뉴 (프리셋 · 도움말 · 다시 시작 · 종료)",
                      "Right-click in full screen",
                      "Picture size, navigator and thumbnail bar instead of the file items, plus the title bar's menu - presets, help, restart, quit"),
                    // 2026-08-17에는 창 모드에서만 나오는 줄이라 조건을 앞에 적었다 -
                    // 부착 상태에서 찾다가 없다고 읽으면 그게 더 나쁘다는 이유였다.
                    // 그 조건이 2026-08-18에 없어졌고(도킹 상태에도 나온다), 이 줄은
                    // 2026-08-19에야 따라왔다. 기능이 넓어질 때 그것을 설명하던 글을
                    // 같이 찾는 것이 이번 판에서 세 번 걸린 일이다.
                    R("전체 화면에서 우클릭 → 바탕화면 채우기",
                      "비활성화하면 창 크기를 그대로 두고 그림만 꽉 채움 (기본은 화면 전체로)",
                      "Right-click in full screen → Fill desktop",
                      "Off keeps the window's own size and fills that instead; on (the default) grows it to the whole screen"),
                    // 이미 되는 것을 적던 줄. 헤더가 사라져도 그 자리는 제목
                    // 표시줄이라 창이 끌리는데, 화면에 아무 표시가 없어서 아는
                    // 방법이 없었음 (2026-08-17, 창 크기를 유지하는 전체화면이
                    // 생기면서 실제로 필요해짐). 대가를 같은 줄에 적은 이유는 그것이
                    // "위에서는 휠이 안 먹는다"의 답이기도 하기 때문.
                    // 도킹 상태가 2026-08-19에 들어왔다. 그전에는 창 모드 전용이라
                    // 줄머리에 그렇게 적혀 있었고, 이제 두 상태에서 다 되므로
                    // 조건이 빠지고 무엇이 달라지는지가 설명으로 갔다.
                    R("전체 화면에서 위쪽 띠 드래그",
                      "창 모드는 창 이동, 도킹 상태는 도킹이 풀리며 이동",
                      "Drag the top strip in full screen",
                      "Moves the window; docked, it undocks and then follows"),
                }),

                new Group(T("이미지", "Images"), new[]
                {
                    R("휠", "확대 · 축소", "Wheel", "Zoom in · out"),
                    R("Ctrl+휠 · Shift+휠", "정밀 확대 축소 - Shift 조합시 더 세밀하게",
                      "Ctrl+wheel · Shift+wheel", "Precision zoom - finer with Shift"),
                    // 영상·음악에는 "재생 형식" 줄이 있는데 그림에는 없었다
                    // (2026-08-16). 목록이 아니라 규칙으로 적는 것은 실제로
                    // 규칙이기 때문 - 이 앱은 그림을 디코드하지 않고 전부 셸에
                    // 넘기므로, 볼 수 있는 것과 탐색기가 미리보기를 만드는 것이
                    // 정확히 같은 집합이다. 바로 아래 SVG 줄이 그 규칙의 뒷면
                    // (PC마다 다른 이유)을 이미 말하고 있어 둘이 짝이 된다.
                    R("표시 형식", "Windows가 미리보기를 만들 수 있는 그림 (PSD · RAW · JXL 등 포함)",
                      "Formats it shows",
                      "Any picture Windows can make a thumbnail of - PSD, RAW, JXL and the rest"),
                    // SVG has no decoder in Windows at all, so this one is the
                    // shell's answer or nothing - which is why the same file
                    // shows on one PC and not another. Said here rather than
                    // left as a mystery; the app cannot promise it either way.
                    R("SVG", "Windows에 렌더링 기능 설치돼 있을 때만 표시",
                      "SVG", "Shows only where Windows has something installed that can draw it"),
                    R("드래그", "확대 상태에서 화면 이동", "Drag", "Move around, when zoomed in"),
                    // What the plate IS, which nothing on screen says - it
                    // appears on its own once there is more picture than panel,
                    // and a small dark square in a corner explains nothing by
                    // itself.
                    R("내비게이터", "확대 시 우측 하단에 표시",
                      "The navigator", "Bottom right, once zoomed in"),
                    R("좌우로 길게 드래그", "이전 · 다음 이미지", "Drag sideways, a good push", "Previous · next image"),
                    // Beside the drag rather than with the film's own row for
                    // these buttons: the two rows are one input doing two
                    // different jobs, and which one happens is decided by what
                    // is on screen. Each belongs with the thing it does.
                    R("마우스 앞 · 뒤 버튼", "이전 · 다음 이미지",
                      "Mouse back · forward buttons", "Previous · next image"),
                    R("더블클릭", "맞춤 ↔ 1:1 전환", "Double-click", "Fit ↔ actual size"),
                    // Says what it gives up, because that is the whole choice
                    // between it and 맞춤: one shows all of the picture, the
                    // other fills the panel.
                    R("채우기", "패널을 꽉 채우고 넘치는 부분은 잘라냄",
                      "Fill", "Fills the panel and crops the overflow"),
                    R("Wheel Click · Enter", "전체 화면 (우클릭 메뉴에서 방식 선택)",
                      "Wheel Click · Enter", "Full screen (pick which kind in the right-click menu)"),
                    R("Esc", "전체 화면 종료", "Esc", "Leave full screen"),
                    // WITH THE KEYS, not up in the group that lists the ways
                    // into the panel. It was there first, on the argument that
                    // it is a way out and belongs beside the title bar's icon -
                    // and the author went looking for it among the keys and did
                    // not find it (2026-08-19). Where someone looks beats where
                    // it classifies. Under Esc because the two are read
                    // together: one leaves the full screen, the other folds the
                    // panel away.
                    R("Backspace", "패널 접기 (패널이 펼쳐져 있을 때)",
                      "Backspace", "Fold the panel away, while it is open"),
                    // 목록의 한 줄 이동은 일부러 안 적었다(사용자, 2026-08-22):
                    // 격자에서 ↑↓가 줄을 걷는 것은 너무 자연스러워서 사소하고,
                    // 재생 중 볼륨 등 다른 ↑↓ 설명과 부딪혀 오히려 헷갈린다.
                    R("↑ ↓", "이전 · 다음 항목", "↑ ↓", "Previous · next item"),
                    R("← →", "썸네일 바 켜져 있을 때 이전 · 다음",
                      "← →", "Previous · next, while the thumbnail bar is open"),
                    // Where someone looks for it: the menu row only appears
                    // over a picture with others beside it, so a folder of one
                    // photo gives no clue the feature exists at all.
                    R("F8 · 우클릭 → 슬라이드 쇼", "폴더의 이미지를 차례로 표시 (2장 이상일 때)",
                      "F8 · Right-click → Slideshow", "Turn the folder's images over, one by one (needs two or more)"),
                    R("슬라이드 쇼 중 클릭 · ↑ ↓ · 트리 선택", "쇼 종료, 보고 있던 사진에서 멈춤",
                      "Click, ↑ ↓, or pick a row while it runs", "Ends the show, staying on the picture you were looking at"),
                    R("F9 · ⋯ → 멀티미디어 패널 → 시계와 날짜", "패널 위에 시각·날짜·요일을 표시",
                      "F9 · ⋯ → Multimedia panel → Clock and date", "Puts the time, date and day over the panel"),
                    // The size is a SHARE of the panel, not a point size, and
                    // the row says so - otherwise a percentage reads as a fixed
                    // size and widening the window looks like it lost the setting.
                    R("⋯ → 멀티미디어 패널 → 시계 크기", "50% ~ 150% (패널 크기에 대한 비율)",
                      "⋯ → Multimedia panel → Clock size", "50% to 150%, as a share of the panel"),
                    // The wallpaper item had NO row at all until now - it has
                    // been in the picture's menu for weeks. Worth a line on its
                    // own account, and more so since 2.1.0: which monitor it
                    // lands on is a rule someone has to be told, because
                    // nothing on screen says the sidebar's own display is the
                    // one being set.
                    R("우클릭 → 배경 설정", "앱이 위치한 모니터의 배경화면으로 지정",
                      "Right-click → Set as wallpaper", "Sets it on the monitor the app is on"),
                }),

                new Group(T("영상", "Film"), new[]
                {
                    R("Space · 화면 클릭", "재생 · 정지", "Space · click the video", "Play · pause"),
                    R("← →", "5초 뒤 · 앞", "← →", "Back · forward 5 seconds"),
                    R("마우스 앞 · 뒤 버튼", "10초 뒤 · 앞", "Mouse back · forward buttons", "Back · forward 10 seconds"),
                    R("같은 영상 재생 시", "마지막 위치부터 이어보기",
                      "Play the same file again", "It carries on from where you left off"),
                    // 이어보기 줄 바로 아래. 그 줄이 "다시 틀면 이어진다"이고 이
                    // 줄이 "그 파일을 어디서 다시 찾는가"라, 둘이 한 이야기의 앞뒤다.
                    R("보다가 다른 폴더로 이동", "트리 하단에 영상 이름 표시, 클릭하면 보던 위치에서 이어보기",
                      "Move to another folder while watching",
                      "The film's name waits at the foot of the tree - press it to carry on where you left off"),
                    R("Home", "처음부터", "Home", "Back to the start"),
                    R("P · Insert", "영상 북마크 표시 · 해제",
                      "P · Insert", "Bookmark this moment, or take it back"),
                    R("우클릭 → 영상 북마크 목록", "표시 위치로 이동 · 전체 삭제",
                      "Right-click → Video bookmarks", "Jump to one, or clear them all"),
                    R("재생 중 ↑ ↓", "볼륨 조절 (일시정지하면 항목 이동으로 복귀)",
                      "↑ ↓ while playing", "Volume - they walk the folder again once it is paused"),
                    R("재생 중 M", "음소거", "M while playing", "Mute"),
                    // The film's own 자름맞춤, which the picture's chip row has
                    // had all along. Says it does NOT carry over, because the
                    // picture's does and someone who met it there will expect
                    // the same.
                    R("우클릭 → 영상 크기 → 채우기", "패널을 꽉 채우고 넘치는 부분은 잘라냄",
                      "Right-click → Video size → Fill",
                      "Fills the panel and crops the overflow"),
                    // 바로 위 줄의 반대편. 그 줄은 창 안에서 영상을 어떻게 놓을지
                    // 정하고, 이 줄은 영상에 창을 맞춘다.
                    R("F · 우클릭 → 창을 영상에 맞춤", "창 높이를 영상 비율에 맞춤, 위아래 검은 띠가 없어짐 (창 모드)",
                      "F · right-click → Fit window to video",
                      "Sets the window's height to the film's proportions - the black bands go (window mode)"),
                    R("우클릭 → 자막", "활성화 · 크기 · 위치 · 싱크 조절",
                      "Right-click → Subtitles", "On, size, position, and sync"),
                    // 싱크만 키를 가진 이유가 줄에 들어 있다: 다른 셋은 한 번
                    // 맞추고 마는 것이고 이것만 맞을 때까지 계속 누르는 것이다.
                    // 눌러 두면 반복되는 것도 적었다 - 크게 밀어야 할 때 몇 번
                    // 누를지 세지 않아도 된다.
                    R("< > (자막 있을 때)", "자막 싱크를 0.5초씩 앞 · 뒤로, 누르고 있으면 계속",
                      "< > (while there are subtitles)",
                      "Subtitle sync half a second at a time - hold either one to keep going"),
                    // 랜딩에서 네 번 내세우는 기능인데 F1에는 한 줄도 없었다
                    // (2026-08-19에 발견). 세 스테퍼는 따로 적지 않는다 - 이 줄이
                    // 여는 곳을 가리키고 나머지는 그 안에서 보인다.
                    R("우클릭 → HDR 색 보정", "밝기 · 채도 · 대비 조절, 흰빛으로 뜨는 HDR 영상 보정",
                      "Right-click → HDR correction",
                      "Brightness, saturation and contrast - for the HDR films that arrive washed out"),
                    // 컨트롤 패널, not 재생 막대 (2026-08-16). The strip is what
                    // a reader calls it, and the app had two names for it in
                    // its own text - this row and the music group below. The
                    // English stays "playback controls" rather than a literal
                    // control panel, which is Windows' own thing.
                    R("전체 화면에서 아래쪽", "컨트롤 패널 표시",
                      "Full screen, point at the bottom", "Bring the playback controls back"),
                    // The rows someone asks about rather than discovers, and the
                    // help is where that answer belongs: this is the page people
                    // are already looking for something on, so saying what does
                    // not work here reads as an answer instead of as a warning
                    // (which is why it is NOT on the download page).
                    //
                    // The extension list is VideoExtensions. WHAT THE ROWS DO
                    // NOT DO is name a codec: a first draft said "mkv does not
                    // play" and "install the Store's HEVC/AV1 extensions", and
                    // the first was falsified within the hour (2026-08-11 - mkv
                    // plays fine on a machine with a codec pack). Playback is
                    // MediaElement, the OLD pipeline, so it uses the DirectShow
                    // filters installed on the PC and not the Store's Media
                    // Foundation extensions - which is exactly why the same file
                    // plays on one machine and not another, and why the honest
                    // row is the one that says so.
                    R("재생 형식",
                      "mp4 · m4v · mov · avi · wmv · mkv · webm · mpg · mpeg · ts · m2ts · mts · flv · 3gp · asf · wm · qt · 3g2 · m2t",
                      "Formats it plays",
                      "mp4 · m4v · mov · avi · wmv · mkv · webm · mpg · mpeg · ts · m2ts · mts · flv · 3gp · asf · wm · qt · 3g2 · m2t"),
                    R("코덱", "외부 코덱 사용 (필요 시 직접 설치)",
                      "Codecs", "Whatever is installed on the PC - add one if a file needs it"),
                    // AV1 by name, because the row above cannot be searched for.
                    // Named as OBSERVED rather than as a rule (video.log,
                    // 2026-08-11: two attempts, no `opened` event at all,
                    // hr=0xC00D11B1) - the codec story is per-machine, so what
                    // is honest is "does not open here", not "never plays".
                    R("AV1", "현재 미지원 (코덱에 따라 다를 수 있음)",
                      "AV1", "Not supported at present - may differ with the codecs installed"),
                    // "트리에서" is not padding: Enter INSIDE the panel is full
                    // screen (see the images group above), and this row sits in
                    // the viewer's own section.
                    R("재생 오류 시", "기본 프로그램으로 열기 권장 (트리에서 더블클릭 · Enter)",
                      "When it will not play", "Better opened in your usual player (double-click or Enter, in the tree)"),
                }),

                // Its own group under 영상 rather than rows inside it: the
                // transport is the same strip, but everything that is
                // DIFFERENT about sound is different because there is no
                // picture - it keeps playing when you look elsewhere, and it
                // does not remember where you stopped.
                new Group(T("음악", "Music"), new[]
                {
                    R("재생 형식", "mp3 · wav · flac · m4a · m4b · aac · wma",
                      "Formats it plays", "mp3 · wav · flac · m4a · m4b · aac · wma"),
                    R("앨범아트 클릭", "재생 · 정지", "Click the album art", "Play · pause"),
                    // 2026-08-17: 앨범아트도 그림과 같은 조절 줄을 받게 되면서
                    // 필요해진 줄. 앞의 줄이 "클릭은 재생"이라고 말하므로 크기를
                    // 어디서 바꾸는지는 바로 옆에 있는 것이 맞다.
                    R("앨범아트 크기", "그림과 같은 줄에서 맞춤 · 1:1 · 채우기 · 확대 축소",
                      "Album art size",
                      "The same row a picture gets - fit, 1:1, crop to fill, and the zoom stepper"),
                    R("컨트롤 패널 왼쪽 두 칩", "백그라운드 재생 · 이어서 재생",
                      "The two chips at the left of the playback controls", "Background play · keep playing"),
                    R("백그라운드 재생", "다른 폴더 이동해도 계속 (비활성화하면 정지 · 파일 해제)",
                      "Background play", "Carries on in other folders - switching it off stops it and frees the file"),
                    R("이어서 재생 우클릭", "폴더 반복 · 한 곡 반복 · 셔플 반복",
                      "Right-click the keep-playing chip", "Repeat folder · repeat one · repeat shuffled"),
                    R("음악 폴더 선택", "▶ 폴더 전체 재생 · 셔플 반복",
                      "Select a folder of music", "▶ play the folder, or repeat it shuffled"),
                    // The condition is the row's reason for existing: without
                    // it, right-clicking a folder that has never been opened
                    // shows nothing and reads as the feature being missing.
                    R("폴더 우클릭 → 재생", "한 번이라도 펼쳐 본 폴더에 나타남",
                      "Right-click a folder → Play", "Appears for folders you have opened at least once"),
                    R("재생 위치", "저장 안 됨 (이어보기는 영상만 지원)",
                      "Where you stopped", "Not remembered - only film carries on from where it was"),
                }),

                new Group(T("썸네일 바", "The thumbnail bar"), new[]
                {
                    R("장수 표시 옆 ▤ 버튼", "아래에 해당 폴더 이미지 목록 표시, 검색 중이면 검색 결과",
                      "The ▤ button beside the counter", "The folder's images as a row - or the results, while searching"),
                    // 없어지는 것을 적는 줄, 그리고 이 파일에서 그 종류는 이것뿐이다
                    // THE WAY BACK COMES RIGHT AFTER THE WAYS IN (2026-08-22).
                    // The list became the default with v2.5.0, so the reader
                    // most likely to arrive here is the one whose bar changed
                    // shape after an update - same reasoning as the full-screen
                    // row above: an unexplained change reads as a fault.
                    R("옵션 → 멀티미디어 패널 → 썸네일 목록으로 보기", "여러 줄 목록(기본)과 한 줄 바 중 선택",
                      "Options → Multimedia panel → Thumbnail grid",
                      "The multi-row list (the default) or the single-row bar"),
                    R("옵션 → 멀티미디어 패널 → 썸네일 아래 파일 이름 표시", "각 썸네일 아래에 한 줄로 표시, 긴 이름은 뒷부분 생략",
                      "Options → Multimedia panel → File names under thumbnails",
                      "One line under each thumbnail; long names are shortened at the end"),
                    // ONE ROW FOR THE TWO GRIPS. The same edge does different
                    // work per shape, and two rows would read as two edges.
                    R("바 위쪽 가장자리 드래그", "목록은 표시되는 줄 수, 바는 칸 크기 조정",
                      "Drag the top edge", "Rows on show in the list; cell size in the bar"),
                    R("목록에서 Ctrl+휠", "썸네일 크기 조정",
                      "Ctrl+wheel over the list", "Thumbnail size"),
                    R("Ctrl+클릭 · Shift+클릭", "여러 개 선택 · 범위 선택 (Ctrl+Shift는 범위 추가, Ctrl+A는 전체)",
                      "Ctrl+click · Shift+click",
                      "Pick several · pick a range (Ctrl+Shift adds a range, Ctrl+A takes all)"),
                    R("선택한 칸에서 우클릭", "선택한 파일 전체를 복사 · 잘라내기 · 삭제",
                      "Right-click a picked cell", "Copy, cut or delete everything picked"),
                    R("칸을 밖으로 드래그", "다른 앱으로 드롭, 여러 개 선택했으면 함께 감",
                      "Drag a cell out", "Drop it into another app; a picked set travels together"),
                    R("목록에 파일 끌어다 놓기", "보고 있는 폴더로 복사",
                      "Drop files onto the list", "They land in the folder on show"),
                    R("옵션 → 멀티미디어 패널", "이미지 썸네일 캐싱 · 캐싱 파일 정리",
                      "Options → Multimedia panel", "Preload thumbnails · clean the cache up"),
                }),
            }),

        new Section(
            T("모양과 설정", "Appearance and settings"),
            new[]
            {
                new Group(string.Empty, new[]
                {
                    R("옵션 → 색상 설정", "직접 선택 · 랜덤 지정", "Options → Color settings", "Pick them, or roll the dice"),
                    // THE THREE THINGS IN THAT WINDOW A LABEL CANNOT TEACH
                    // (2026-08-16). The chain is the clearest case this file's
                    // own rule has: a small link mark sits on all seventeen
                    // rows, so it is always on screen and means nothing until
                    // someone is told - including the half that catches people
                    // out, that BOTH ends have to be lit.
                    //
                    // 그림자 is here for WHERE it is. It is a behaviour toggle
                    // living in the colour window on purpose (among the
                    // behaviour toggles the word reads as a shadow around the
                    // whole app), and the cost of that choice is that anyone
                    // hunting the options menu for it never finds it.
                    R("색상 설정 → 행의 고리", "색을 묶어 함께 변경, 함께 활성화한 줄끼리 같은 색",
                      "Color settings → the link on a row", "Ties rows together - the ones lit together share a color"),
                    R("색상 설정 → 모노", "팔레트 전체를 회색톤으로 한 번에",
                      "Color settings → Mono", "The whole palette to grayscale in one press"),
                    R("색상 설정 → 그림자", "트리와 북마크 패널의 위아래 끝을 옅게 덮음",
                      "Color settings → Shading", "Veils the top and bottom ends of the tree and the bookmark panel"),
                    R("옵션 → 기본 설정", "자동 시작 · 트레이 · 아이콘 · 자동 숨김", "Options → General", "Autostart · tray · icons · auto-hide"),
                    // "아이콘" in the row above reads as ONE switch, and it is
                    // three. The drive one arrived last (2026-08-16) and the
                    // release notes say it is separate from the folder's, so the
                    // help has to say the same - a reader who took the row above
                    // at its word would go looking for a switch that is already
                    // there under a name it does not use.
                    R("옵션 → 기본 설정 → 아이콘", "폴더 · 파일 · 드라이브를 따로따로 활성화/비활성화",
                      "Options → General → Icons", "Folders, files and drives switch on and off separately"),
                    R("옵션 → 기본 설정 → 드래그로 이동", "비활성화하면 드래그는 항상 복사, 이동은 Shift",
                      "Options → General → Drag Moves", "Off, a drag always copies and Shift is the way to move"),
                    // A where, not a whether (2026-08-30): the strip is on either
                    // way and both seats carry the same controls, so the row says
                    // where the line goes and nothing about losing anything.
                    R("옵션 → 기본 설정 → 경로 표시줄 위에 표시", "경로 줄을 트리 위로 이동",
                      "Options → General → Show path bar at top", "Moves the path line above the tree"),
                    // The cost goes on the row, not in a paragraph: it is the
                    // one setting here that can make the tree slower.
                    R("옵션 → 폴더에 표시할 항목 개수 → 전체 표시", "더 보기 없이 전부, 큰 폴더에서는 느려짐",
                      "Options → Items per folder → Show all", "No show more row at all - a big folder will feel it"),
                    R("옵션 → 네트워크 위치", "드라이브 문자 없는 공유 추가 (\\\\서버\\공유)",
                      "Options → Network locations", "Add a share with no drive letter (\\\\server\\share)"),
                }),
            }),
    };
}
