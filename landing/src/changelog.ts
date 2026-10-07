// What the "업데이트 내역" card above the download buttons shows, newest first.
//
// Kept here rather than fetched from the GitHub release body on purpose: those
// notes run several paragraphs per item in two languages, which is the wrong
// shape for a card someone glances at on the way to the download button. Three
// short lines per version was the shape to aim for; a release that genuinely
// carries a fourth thing worth stopping for can take a fourth line (v1.4.0,
// the user's call). It is a glance, not a list - don't let it grow past that.
//
// FROM v2.1.0 the ceiling is softer, because fixes are named now rather than
// summed up in one line (see that entry for why). A round that fixed five
// things a person could actually have hit gets five lines; a round that fixed
// one gets one. Every line is still a GLANCE - a few words, never a paragraph.
//
// A fix line may name the PROBLEM where that is what makes it recognisable -
// an intermittent one especially, since the person who hit it knows it by the
// symptom and by nothing else. Two judgements go with that. It is written from
// the outside, in what someone SAW, never in the app's internals. And a
// symptom that would frighten a reader who never hit it stays out of the card
// altogether rather than being softened into vagueness: the fix ships either
// way, and this list sits directly above a download button.
//
// A RENAME IS A LIST ITEM, NOT A STORY (v2.4.1, the author's call: 사소한걸
// 너무 풀어서 설명해 준 느낌이 강해서 민망스럽다). "X → Y" is the whole line.
// WHY that name was picked is our side of it, not the reader's: 계열을 맞췄다,
// 국문과 겹치는 낱말이 없었다 - all of it stays in the commit message and TODO,
// where the reasoning is actually useful. The same goes for the GitHub release
// notes, which had grown two paragraphs around a one-line fix; they are a list
// now too, and a one-line fix does not get a section heading of its own.
//
// The exception is narrow: keep the explanation only where WITHOUT it nobody
// can tell what was fixed - a window that had been contradicting itself, or a
// dialog title that said something had already happened when it had not.
//
// One rule when releasing: add an entry here in the same pass that bumps the
// csproj. If this list falls behind, the card notices - the section only shows
// its lines when the newest entry matches the version GitHub reports as latest
// (see UpdateNotes.vue), so a forgotten entry costs a hidden card rather than a
// landing page claiming the wrong thing.
export interface ChangelogEntry {
  version: string
  ko: string[]
  en: string[]
}

export const changelog: ChangelogEntry[] = [
  {
    // TWELVE LINES: four additions, four changes (the bookmark landing is
    // written as what now works, not as a fault), four fixes of faults that
    // were in v2.6.2. The release notes carry five more: the marked count on
    // the menus, marked thumbnails naming themselves, the search keeping its
    // place and not re-reading the same folder, 붙여넣기 standing down on the
    // thumbnail menu during a search, and two fixes (the first Shift+click on
    // the thumbnails, a rename ending in a space or a dot).
    //
    // Off the card and the notes both: everything that repaired this
    // release's own new code before it shipped - the SVG renderer's leak and
    // its declining rules, the search's Shift range and menu fixes - and the
    // internal cleanups.
    //
    // The NAS line names no maker. The cause was one vendor's folder, but the
    // card speaks of what someone saw, and "일부" keeps it from claiming more
    // than was fixed.
    version: 'v2.6.3',
    ko: [
      '검색 결과에서 `Ctrl`·`Shift`+클릭으로 여러 개를 선택할 수 있습니다. 복사, 잘라내기, 삭제, 드래그가 선택한 파일 전체에 적용됩니다.',
      '검색 결과에서 이름, 크기, 수정 시각이 같은 파일은 연한 배경으로 표시됩니다.',
      '트리의 툴팁에 파일 크기와 수정 시각이 표시됩니다.',
      '멀티미디어 패널의 그림을 클릭하면 썸네일 목록이 보고 있는 그림 위치로 이동합니다.',
      '`자동 펼치기`가 검색 결과에서도 동작합니다.',
      '아이콘처럼 단순한 SVG는 멀티미디어 패널과 썸네일 목록에 항상 표시됩니다.',
      '이름이 같은 파일 여러 개를 한 번에 드롭하면 덮어쓰기를 묻지 않고 번호를 붙여 저장합니다.',
      '트리가 화면에 모두 들어오는 경우에도 북마크로 이동하면 대상이 바로 맨 위에 표시됩니다.',
      '일부 NAS 폴더를 검색할 때 `다시 인덱싱`의 파란 점이 변경이 없어도 켜지던 문제를 수정했습니다.',
      '새로고침이나 정렬 변경 후 트리에서 여러 개를 선택하면 썸네일 목록에 표시되지 않던 문제를 수정했습니다.',
      '사용자 폴더(`C:\\Users\\이름`)의 `속성`이 열리지 않던 문제를 수정했습니다.',
      '자동 숨김 상태에서 빈 멀티미디어 패널이 함께 펼쳐지던 문제를 수정했습니다.',
    ],
    en: [
      'Pick several search results with Ctrl+click or Shift+click. Copy, cut, delete and drag apply to every picked file.',
      'Search results with the same name, size and modified time are faintly shaded.',
      'Tree tooltips now show the file\'s size and modified time.',
      'Clicking the picture in the multimedia panel scrolls the thumbnail list back to it.',
      'Expand on selection now works from search results too.',
      'Simple, icon-style SVGs now always show in the multimedia panel and the thumbnail list.',
      'Dropping several files with the same name numbers them instead of asking to overwrite.',
      'A bookmark jump now lands at the top at once, even when the whole tree fits the window.',
      'Fixed the Reindex button\'s blue dot turning on with no change in some NAS folders.',
      'Fixed marks made in the tree not showing on the thumbnail list after a refresh or a sort change.',
      'Fixed Properties not opening for the user folder (C:\\Users\\name).',
      'Fixed an empty multimedia panel unfolding with the sidebar in auto-hide.',
    ],
  },
  {
    // TEN LINES, kept at ten on the author's reading of the draft. The four
    // additions lead - two of them the thumbnail list's own, the names under
    // the cells and renaming from them - then the four changes someone will
    // notice in use, then the two fixes. The notes carry
    // three more changes than the card: the F7 folder landing at the top of
    // the view, a right-click on an open menu doing nothing, and the tree
    // menu's shortcuts working while it is open.
    //
    // Off the card and the notes both: the About window's MagicLoupe link (the
    // v2.5.9 SweepCap precedent), the search index's integrity fix (it repaired
    // this release's own background re-index), and the thumbnail menu's
    // right-button handling - its symptom reads as alarming, and what a reader
    // can see of the change is the notes' line about an open menu.
    //
    // The ↑↓ half of the side-last-clicked line is scoped to the thumbnail
    // list (썸네일 목록) because that is the only shape it applies to: in the
    // bar the arrows stay the tree's.
    version: 'v2.6.2',
    ko: [
      '썸네일 아래에 파일명이 표시됩니다. `옵션 → 멀티미디어 패널 → 썸네일 파일명 표시`에서 비활성화할 수 있습니다.',
      '보고 있는 썸네일의 파일명을 한 번 더 클릭하거나 `F2`를 누르면 썸네일에서 바로 이름을 변경할 수 있습니다.',
      '`Ctrl+Shift+F`로 트리에서 선택한 폴더를 바로 검색할 수 있습니다.',
      '트리 우클릭 메뉴에 `전체 선택`이 추가되었습니다. 단축키는 `Ctrl+A`입니다.',
      '`Ctrl+A`, `F2`와 썸네일 목록의 `↑` `↓`는 트리와 썸네일 중 마지막으로 클릭한 쪽에서 동작합니다.',
      '썸네일을 우클릭하면 멀티미디어 패널과 트리도 그 파일로 이동합니다.',
      '검색을 열면 저장된 결과를 먼저 표시하고, 필요하면 자동으로 다시 인덱싱해 결과를 갱신합니다.',
      '`속성` 창이 우클릭한 위치에 열리고, 자동 숨김이나 `항상 위에 표시` 상태에서도 앱 뒤에 가려지지 않습니다.',
      '파일 이름을 변경하면 상위 폴더가 선택되던 문제를 수정했습니다.',
      '`탐색기에서 위치 열기`로 열린 탐색기 창에서 파일이 아래 끝에 걸쳐 보이던 문제를 수정했습니다.',
    ],
    en: [
      'File names now show under the thumbnails. Options → Multimedia panel → File names under thumbnails turns them off.',
      'Rename a file from the thumbnails: click the name of the one on show again, or press F2.',
      'Ctrl+Shift+F searches the folder selected in the tree.',
      "Select all is now on the tree's right-click menu. Ctrl+A does the same.",
      'Ctrl+A, F2 and, in the thumbnail grid, ↑↓ act on whichever of the tree and the thumbnails was clicked last.',
      'Right-clicking a thumbnail now moves the multimedia panel and the tree to that file.',
      'Opening a search shows the saved results first, then re-indexes when needed and updates them.',
      'The Properties window opens where you right-clicked, and no longer hides behind the app when it stays on top.',
      'Fixed the parent folder getting selected after renaming a file.',
      'Fixed Reveal in Explorer leaving the file half-hidden at the bottom of the Explorer window.',
    ],
  },
  {
    // SIX LINES, the longest card in a while, kept at six on the author's
    // reading of the draft. The two additions lead, then the three changes
    // someone will notice in use, then the one fix.
    //
    // The menu line is written as an improvement although it closes a defect:
    // every menu in the window had been stopping at 600px since the cap was
    // added, because the computed value was written to a dictionary the menus
    // never read. Saying so here would tell a reader who never noticed that it
    // was wrong for months, directly above the download button - the fix ships
    // either way, and the commit carries the account.
    //
    // The multimedia panel line holds two behaviours (a sign-in start, and an
    // auto-hide with nothing to show) because both are the same promise: the
    // panel does not unfold when there is nothing in it to look at.
    version: 'v2.6.1',
    ko: [
      '썸네일 우클릭 메뉴에 `속성`이 추가되었습니다.',
      '트리에서 파일을 우클릭하면 `폴더로 이동`으로 그 파일이 있는 폴더로 바로 이동할 수 있습니다. 단축키는 `Alt+↑`입니다.',
      '우클릭 메뉴가 화면 높이만큼 표시됩니다. 화면에 들어가는 메뉴는 스크롤 없이 모두 표시됩니다.',
      '재생 중 표시와 재생/일시정지 버튼이 더 잘 보이도록 변경되었습니다.',
      '`부팅 후 자동 시작`으로 실행되거나, 보여 줄 파일이 없는 상태에서 자동 숨김되면 멀티미디어 패널이 닫힌 상태로 표시됩니다.',
      '투명한 모서리가 있는 PNG 썸네일의 가장자리가 깨져 보이던 문제를 수정했습니다.',
    ],
    en: [
      "Properties is now on a thumbnail's right-click menu.",
      'Right-click a file in the tree and Go to folder takes you to the folder it sits in. Alt+↑ does the same.',
      'Right-click menus now use the height of the screen, so a menu that fits shows everything without scrolling.',
      'The playing indicator and the play/pause button are easier to see.',
      'The multimedia panel starts closed when launched by Start with Windows, and folds on auto-hide when there is nothing in it to show.',
      'Fixed the edges of PNG thumbnails with transparent corners looking broken.',
    ],
  },
  {
    // TWO FIXES, AND THE MINOR NUMBER MOVED ANYWAY (the author's call): the
    // patch place would have gone to two digits, which they have declined
    // before (v1.0.11 became v1.1.0 for the same reason).
    //
    // The picture line leads because it is the one anyone can see - it was
    // reported as a clearly visible loss, found by putting the same file beside
    // an image editor. It says WHAT WAS WRONG (half a pixel off) because a reader
    // who noticed their screenshots looking soft has no other way to recognise
    // their own case.
    //
    // The crash line stays as a crash line, the author's call. The standing
    // rule keeps a frightening symptom off a card that sits above a download
    // button, but this one carries its own condition - another program holding
    // the clipboard - so the reader who never hit it can see that it is not
    // waiting for them, and the one who did hit it needs no translation. It
    // came in as issue #3 with the diagnosis and the fix already in it.
    version: 'v2.6.0',
    ko: [
      '1:1 배율에서 이미지가 화면 픽셀에 정확히 맞춰 표시됩니다. 이전에는 반 픽셀 어긋난 자리에 그려져 선명도가 떨어질 수 있었습니다.',
      '경로 복사 시 다른 프로그램이 클립보드를 사용 중이면 앱이 종료되던 문제를 수정했습니다.',
    ],
    en: [
      'A picture at 1:1 now lands on whole screen pixels. It could sit half a pixel off before, which softened every edge.',
      'Fixed the app closing when a path was copied while another program held the clipboard.',
    ],
  },
  {
    // FOUR LINES: one thing to see, three things that were wrong. The panel
    // line LEADS because it is the only change of the round anyone can look at
    // - the other three are recognised by people who met them.
    //
    // The panel line is the author's own wording, and shorter than the draft
    // it replaced: 숨겨진 covers both reasons a bookmark can be missing from
    // the tree (the file-kind filter, and 숨기기), so naming the two mechanisms
    // separately was spending three clauses on a distinction the reader does
    // not have to make.
    //
    // The auto-collapse line went on at the author's call. It is the one item
    // here nobody may have noticed - the option quietly stopped applying until
    // the next jump - and it is on the card because someone who DID notice has
    // no other way to learn it is fixed.
    //
    // Off the card: the F1 rows, the About link, and the check-text addition.
    version: 'v2.5.9',
    ko: [
      '북마크 패널에서 숨겨진 항목이 흐리게 표시됩니다. 숨겨진 북마크 폴더는 우클릭으로 바로 해제할 수 있습니다.',
      '트리에서 다른 폴더로 이동할 때 목록 아래에 빈 공간이 생기던 문제를 수정했습니다.',
      '북마크나 폴더로 이동할 때 화면이 두 번 움직이던 것이 한 번에 정리됩니다.',
      '폴더 자동 접기가 간헐적으로 동작하지 않던 문제를 수정했습니다.',
    ],
    en: [
      'The bookmark panel now mutes an entry the tree is not showing. Right-click an excluded bookmark folder to stop excluding it from there.',
      'Fixed an empty area appearing below the tree after moving to another folder.',
      'A jump to a bookmark or a folder now places the row in one movement instead of two.',
      'Fixed Auto-collapse folders intermittently not running.',
    ],
  },
  {
    // THREE LINES OUT OF FOUR COMMITS, and the two the search sort round took
    // are ONE line here. Splitting the grouping from the sort and putting the
    // folder onto the file rows are the same change seen from two sides - a
    // reader glancing at this card wants what the search list now does, not
    // how many commits it took.
    //
    // Off the card: the fourth commit repaired a width the round itself had
    // just introduced, which the standing rule keeps out, and a landing card
    // line for v2.5.7 that had already shipped.
    //
    // The sort line LEADS because it is what the round is. The header click is
    // the smaller half of the same screen, and the fix is a leftover from the
    // bookmarked-file work v2.5.7's card already carried - which is exactly
    // why it is worth a line: the people reading this card are the ones who
    // read that one.
    version: 'v2.5.8',
    ko: [
      '검색 결과의 `폴더별 묶기`와 정렬 기준이 각각 독립적으로 동작합니다. 묶은 상태에서도 `이름`·`수정한 날짜`와 `오름차순`·`내림차순`을 선택할 수 있고, 묶기를 해제하면 파일마다 폴더 경로가 함께 표시됩니다.',
      '검색 결과의 폴더 머리글을 클릭하면 트리에서 해당 폴더로 이동합니다. `Enter`로도 동일하게 동작합니다.',
      '북마크에 추가한 파일에 펼침기호가 표시되던 문제를 수정했습니다.',
    ],
    en: [
      'Group by folder and the sort order in the search results now work independently. Name · Date modified and Ascending · Descending stay available while results are grouped, and with the grouping off each file row carries its folder path on the right.',
      'Click a folder header in the search results to go to that folder in the tree. Enter does the same.',
      'Fixed a bookmarked file showing an expand arrow in the tree.',
    ],
  },
  {
    // ONE FEATURE, TWO FIXES. The round went out as 1+1 on the author’s count
    // and the first-click-collapse line was added the same evening, on their
    // own second thought ("필요할것 같아서요") - the option it touches shipped
    // only one release earlier, so the people most likely to have met the
    // quirk are exactly the ones reading this card. Off the card: a
    // .gitignore line.
    //
    // The fix line names the SYMPTOM (an item reported missing) and not what
    // the dialog went on to offer (removing the bookmark): the offer is the
    // scary half, and the standing rule keeps that out of a list that sits
    // above a download button.
    version: 'v2.5.7',
    ko: [
      '옵션 → 기본 설정에 `경로 표시줄 위에 표시`가 추가되었습니다. 활성화하면 경로 표시줄이 하단 대신 헤더 바로 아래에 표시됩니다. 검색 화면에서도 같은 위치를 유지합니다.',
      '북마크에 추가한 파일을 클릭하면 항목을 찾을 수 없다는 안내가 표시되던 문제를 수정했습니다. 폴더와 파일 모두 정상적으로 이동합니다.',
      '`클릭 시 바로 펼침`을 비활성화한 상태에서 북마크로 이동한 폴더를 트리에서 클릭하면 바로 접히던 문제를 수정했습니다. 첫 클릭은 선택만 유지하고 두 번째 클릭이 접습니다.',
    ],
    en: [
      'A new setting, Show path bar at top, in Options → General. Switch it on and the path bar sits directly under the header instead of at the bottom. It keeps that place in the search view too.',
      'Fixed clicking a bookmarked file reporting that the item could not be found. Folders and files both navigate correctly now.',
      'Fixed a folder reached through a bookmark collapsing on its first tree click with Expand on a single click off. The first click keeps the selection, and the second one collapses.',
    ],
  },
  {
    // SIX LINES OUT OF TWELVE COMMITS, and most of what was cut was never for
    // this card. Four of them are invisible from outside the app: a DEBUG-only
    // log line, a new check-text rule, and two landing changes (the page picks
    // the browser's language now, and its footer carries a third tool). None of
    // them is something a person could recognise having seen.
    //
    // BOTH FIX LINES ARE OLDER THAN THIS ROUND, which is the standing rule - a
    // fix for something the round itself introduced would say the new features
    // shipped broken. The rename box had carried the dark theme's four colors
    // since before there was a light theme to take them from, and the check in
    // the thumbnail mark had been white since the mark was drawn.
    //
    // The first line leads because of what the round turned out to be worth in
    // use rather than what order it was built in (2026-08-29).
    version: 'v2.5.6',
    ko: [
      '옵션 → 기본 설정에 `클릭 시 바로 펼침`이 추가되었습니다. 이 설정을 끄면 폴더의 첫 클릭은 선택, 두 번째 클릭이 펼치기·접기가 됩니다.',
      '트리 우클릭 메뉴에 `바로 가기 만들기`가 추가되었습니다. 여러 항목을 선택한 경우 항목마다 하나씩 만들어집니다.',
      '`Shift+Backspace`로 펼친 폴더를 모두 접을 수 있습니다. 전체 접기 아이콘의 `Shift+클릭`과 같습니다.',
      '옵션 → 기본 설정의 `내 PC 아이콘 표시`로 제목 표시줄의 아이콘을 윈도우의 내 PC 아이콘으로 변경할 수 있습니다.',
      '라이트 모드에서 썸네일 선택 표시의 체크가 잘 보이지 않던 문제를 수정했습니다. 체크 색은 선택 색상에 맞춰 결정됩니다.',
      '라이트 모드에서 이름 바꾸기 입력창이 어둡게 표시되던 문제를 수정했습니다.',
    ],
    en: [
      'A new setting, Expand on a single click, in Options → General. Switch it off and the first click on a folder selects it, while the second one expands or collapses.',
      'Create shortcut has been added to the tree’s right-click menu. With several items selected it makes one for each.',
      'Shift+Backspace collapses every expanded folder, the same as Shift+clicking the collapse-all icon.',
      'Show This PC icon, in Options → General, puts Windows’ own This PC icon in the title bar.',
      'Fixed the check on a selected thumbnail being hard to see in light mode. Its ink is now taken from the selection color.',
      'Fixed the rename box appearing dark in light mode.',
    ],
  },
  {
    // SEVEN LINES OUT OF A ROUND OF THIRTY COMMITS.
    //
    // THE RANGE IS THE TAG, NOT THE UNPUSHED SET, and getting that wrong is how
    // this card was first written four lines short. Twenty-one commits were
    // sitting local; nine more had been pushed on 08-26 and never shipped, and
    // the biggest thing in the release - the band that marks how far the folder
    // you are in reaches, and being able to set its colour - was among them.
    // The author noticed it was missing. Diff from `v2.5.4..HEAD`, never from
    // what `git log origin/main..HEAD` happens to show.
    //
    // The cuts are still worth recording: the author took out the English
    // review (it goes in the release notes instead) and the divider line that
    // survives 영역 구분선 being switched off; the snap grid, the ScrollBar's
    // context menu, the 정렬 기본값 move and the colour window's button layout
    // never made the draft.
    //
    // TWO FIX LINES, and every other fix in this round is left out on the
    // standing rule: they repaired something the round itself introduced, and
    // listing those says the new features shipped broken. Both that stayed are
    // older than this round.
    //
    // THE ONE THAT STAYED IS OLD, and it is here in the AUTHOR'S framing rather
    // than in the symptom's. The first draft named what was actually offered -
    // a whole drive copied into the folder it landed on - and was cut under the
    // v1.2.0 judgement, that a symptom which alarms a reader who never hit it
    // does not belong directly above a download button. The author put it back
    // differently: name the CAUSE (a slight drag while moving folders), not the
    // consequence. "신뢰감도 주고" - a list with no fixes in it reads as a
    // release that fixed nothing, and the cause is the half a reader can
    // actually recognise in their own hands. The consequence is in the release
    // notes, where whoever wants it has already arrived.
    version: 'v2.5.5',
    ko: [
      '선택한 항목이 들어 있는 폴더의 영역에 배경색이 표시되어, 지금 어느 폴더 안에 있는지 한눈에 보입니다. 색상 설정에서 이 영역의 배경색을 별개로 지정할 수 있습니다.',
      '트리 하단의 재생 줄에 재생/일시정지 버튼이 추가되었습니다. 음악이 재생 중일 때는 움직이는 표시로 나타납니다.',
      '숨긴 폴더 목록에 각 폴더가 있던 드라이브가 함께 표시됩니다. 네트워크와 클라우드 위치는 아이콘의 표시로 구분됩니다.',
      '숨긴 폴더만 들어 있는 폴더가 `비어 있음`으로 표시되지 않습니다. 폴더를 삭제할 때 안에 숨긴 폴더가 있으면 개수를 알려 줍니다.',
      '썸네일 바/목록의 툴팁이 더 빠르게 나타납니다.',
      '폴더 우클릭의 `기본 프로그램에서 열기`가 폴더에서는 `펼치기`·`접기`로 표시됩니다. 이미 펼친 폴더에서 이 줄과 `Enter`가 동작하지 않던 문제를 수정했습니다.',
      '폴더 이동 시 미세한 드래그로 발생하던 문제를 수정했습니다.',
    ],
    // THE ENGLISH WENT THROUGH A NATIVE REVIEW OF ITS OWN (2026-08-27), and
    // three of its notes are worth keeping. "marks how far the folder reaches"
    // reads as distance rather than extent; a single verb carrying both the
    // buttons and the moving mark packs two unlike objects onto one hinge; and
    // "holding nothing but an excluded folder" was singular where the feature
    // counts any number.
    //
    // TWO OF ITS EDITS WERE NOT TAKEN. It guessed the menu's English label as
    // "Open with default program" - the app says `Open in default app`, and a
    // card naming a label has to name the one on screen. And it read `color`
    // against `colour`, which is right and is now checked for: the landing card
    // was in none of the five files check-text scanned for British spellings,
    // so those two `colour`s were on their way out with the release.
    en: [
      'A faint band now marks the full extent of the folder you are in, so you can see at a glance where you are. Its background color can be set separately in the color settings.',
      'The playback row at the foot of the tree now has play and pause beside the stop button, and shows a moving mark while music is playing.',
      'The list of excluded folders now shows which drive each folder is on. Network and cloud locations are marked on the icon.',
      'A folder that contains nothing but excluded folders is no longer shown as empty. Deleting a folder now tells you how many excluded folders are inside it.',
      'Tooltips in the thumbnail bar and list now appear faster.',
      'In a folder’s right-click menu, Open in default app now reads Expand or Collapse. Fixed a problem where that row and Enter did nothing on a folder that was already expanded.',
      'Fixed a problem caused by a slight unintended drag when moving a folder.',
    ],
  },
  {
    // The column-count flip is NOT a line, and that is the same judgement the
    // margin fix got in v2.5.3: nobody using the app noticed it, the author
    // included - it was the brake's own log that found it. A card line is what
    // someone could recognise having SEEN.
    version: 'v2.5.4',
    ko: [
      '펼친 폴더가 비어 있으면 `비어 있음`, 파일 형식 필터가 파일을 모두 감춘 경우에는 감춰진 파일 개수가 폴더 안에 표시됩니다.',
      '트리 우클릭 메뉴에서 해당되지 않는 항목이 흐리게 표시되지 않던 문제를 수정했습니다. 파일에서의 `숨기기`, zip이 아닌 파일에서의 `압축 풀기` 등이 해당합니다.',
      '다른 앱으로 파일을 끌어놓는 중 그 앱에서 오류가 발생하면 Edgetree도 함께 종료되던 문제를 수정했습니다.',
      '트리 우클릭의 `이 폴더 숨기기`가 `숨기기`로 바뀌었습니다. 드라이브와 여러 항목에도 같은 표기를 사용합니다.',
      '우클릭 메뉴의 `속성`을 `Alt+Enter`로 바로 열 수 있습니다.',
      '메뉴가 화면보다 길 때 아래에 내용이 더 있다는 표시가 바로 나타나지 않던 문제를 수정했습니다.',
    ],
    en: [
      'An expanded folder says when it is empty, and when the file type filter is what emptied it, how many files it is hiding.',
      'Fixed rows in the tree’s right-click menu not greying out when they do not apply - Hide on a file, Extract on something that is not a zip, and others.',
      'Fixed Edgetree closing along with another application when that application failed while a file was being dragged into it.',
      'Hide This Folder in the tree’s right-click menu is now Hide, which is also what it says for a drive or several items.',
      'Alt+Enter opens Properties straight from the tree, the open menu or a search result.',
      'Fixed the mark that says a menu continues past the screen not appearing until the wheel was touched.',
    ],
  },
  {
    // v2.5.3 CARRIES v2.5.2's CARD TOO, the same call made for the v2.5.0/v2.5.1
    // pair and for the same reason: v2.5.2 shipped this morning with seven lines
    // of features and a one-line patch card on top of it hours later would have
    // buried the release the author actually shipped today. The label stays
    // exactly 'v2.5.3' - UpdateNotes.vue hides the whole card unless the newest
    // entry's version EQUALS the tag GitHub reports as latest, so a merged
    // "v2.5.2~2.5.3" label would read well and blank the card.
    //
    // The v2.5.2 entry is GONE rather than left below: it would be this card
    // minus one line, and the arrows would step between two near-identical
    // pages. The per-release record lives in the READMEs' changelog and the
    // GitHub release notes, which keep both.
    //
    // The new fix goes LAST, with the other fix - features first is the order
    // every entry here uses. The caption's bottom margin is still not a line
    // (사소함).
    version: 'v2.5.3',
    ko: [
      '폴더에 지정한 정렬이 하위 폴더에도 적용됩니다. 하위 폴더에 개별 정렬을 지정한 경우에는 그대로 유지됩니다.',
      '제목 표시줄의 `전체 접기` 아이콘을 `Shift`+클릭하면 펼침 상태를 저장하지 않고 접습니다.',
      'USB, 클라우드 드라이브를 연결하거나 해제하면 트리에 자동으로 반영됩니다.',
      '썸네일 목록에서 선택된 항목의 가시성을 높였습니다.',
      '음악을 분리 재생 중일 때 제목 우측의 X로 재생을 종료할 수 있습니다.',
      '`숨김·시스템 항목 표시`가 `옵션 → 기본 설정`으로 이동했습니다.',
      '트리에서 `더 보기`에 가려진 항목을 썸네일에서 클릭해도 선택되지 않던 문제를 수정했습니다.',
      '앱 전체화면을 사용한 뒤 멀티미디어 패널의 파일 정보가 썸네일 바 위에 겹쳐 보이고 패널 높이가 흔들리던 문제를 수정했습니다.',
    ],
    en: [
      'A folder’s sort order now applies to its subfolders. A subfolder with its own sort keeps it.',
      'Shift-clicking the Collapse All icon in the title bar folds without storing the expanded state.',
      'Connecting or removing a USB or cloud drive updates the tree automatically.',
      'The selected cell in the thumbnail list is easier to pick out.',
      'An X beside the title ends detached audio playback.',
      'Show Hidden and System Items moved to Options → General.',
      'Clicking a thumbnail for an item hidden behind Show More in the tree now selects it.',
      'Fixed the multimedia panel’s file details overlapping the thumbnail bar, and the panel height shifting, after using full screen.',
    ],
  },
  {
    // v2.5.1 CARRIES THE ROUND'S WHOLE CARD, because v2.5.0 lived two hours
    // and reached two downloads before this superseded it - a two-line patch
    // card on top would have buried the feature release the author actually
    // shipped today (their report, 2026-08-22: "새 기능들 랜딩카드가 다
    // 숨겨져서"). The crash fix is deliberately NOT a line: it repaired
    // something introduced the same day that effectively nobody had, and the
    // v2.4.2 rule applies - listing it says the new list shipped broken. The
    // refresh line stays: the strip (and the bar before it) never re-asked
    // about a changed file, so it is new behaviour, not a same-day repair.
    // The v2.5.0 entry below stays as history for the arrows.
    version: 'v2.5.1',
    ko: [
      '썸네일 바에 세로로 스크롤되는 `썸네일 목록` 배치가 추가되었으며, 기본값으로 적용됩니다.',
      '썸네일 크기는 `Ctrl`+휠, 표시되는 줄 수는 경계 드래그로 조정할 수 있습니다.',
      '썸네일 목록에서 여러 파일을 선택해 복사, 잘라내기, 삭제, 밖으로 드래그할 수 있습니다.',
      '썸네일이 없는 파일은 종류 아이콘으로 표시됩니다.',
      '새로고침 시 내용이 변경된 파일의 썸네일이 갱신됩니다.',
      '썸네일이 간혹 거꾸로 표시되던 문제를 수정했습니다.',
      '썸네일을 클릭해도 이미지가 바뀌지 않던 문제를 수정했습니다.',
    ],
    en: [
      'The thumbnail bar can lay its pictures out as a scrolling list, now the default.',
      'Ctrl+wheel sizes the thumbnails; dragging the edge shows more rows.',
      'Select several pictures and copy, cut, delete or drag them out together.',
      'Files with no picture of their own show their file-type icon.',
      'Refreshing renews the thumbnails of files whose content changed.',
      'Thumbnails no longer come up upside down now and then.',
      'A thumbnail click no longer fails to change the picture.',
    ],
  },
  // v2.5.0 HAS NO ENTRY OF ITS OWN - the one above is the merged card for the
  // 2026-08-22 pair (the author's call: "랜딩카드를 합칠까요 이번엔? 랜딩에서만").
  // It lived two hours before v2.5.1 superseded it, so a separate entry would
  // show the arrows two near-identical cards. The label stays exactly 'v2.5.1'
  // because UpdateNotes.vue hides the whole card unless the newest entry's
  // version EQUALS the tag GitHub reports as latest - a "~v2.5.1" range label
  // would read nicely and blank the card. The per-release record lives in the
  // READMEs' changelog and the GitHub release notes, which keep both.
  {
    // ELEVEN LINES IN THE RELEASE NOTES, SIX HERE. What came out is what a
    // person finds while using the app rather than while deciding to download
    // it: the panel folding on Backspace, the thumbnail bar stepping aside in
    // full screen, the play button following the pointer. All three are real
    // and none of them would move anyone's hand toward the button.
    //
    // The three subtitle items are one line. Size, sync and position arrived
    // together and are read together; three lines would have made a card about
    // subtitles.
    //
    // NO FIX LINES THIS TIME, and that is not an omission. Everything repaired
    // in this round was a repair to something else in this round - the docked
    // full screen, the thumbnail bar, the subtitle scale - so none of it ever
    // reached anyone. Listing them would say the new features shipped broken.
    //
    // The two lists are the same length here, unlike v2.4.1: every line is
    // true on both screens this time.
    version: 'v2.4.2',
    ko: [
      '시청 중이던 영상이 트리 하단에 표시되며, 선택하면 이전 재생 위치부터 재생됩니다.',
      '자막 크기가 영상 크기에 연동됩니다. `<` `>` 로 싱크를, 메뉴에서 위치를 조정할 수 있습니다.',
      '고정 상태에서도 `바탕화면 채우기`를 사용할 수 있습니다.',
      '`F` 키로 창 크기를 영상 비율에 맞게 조정할 수 있습니다.',
      '마우스 앞·뒤 버튼으로 폴더 이력과 이전·다음 이미지를 이동합니다.',
      '재생 볼륨이 저장됩니다.',
    ],
    en: [
      'The film you were watching stays at the foot of the tree, and picks up where you left it.',
      'Subtitles scale with the film. < and > shift the sync; the menu sets their position.',
      'Fill the desktop without undocking first.',
      'F fits the window to the film, so the black bands go.',
      'The mouse thumb buttons move through folders, and through pictures.',
      'The playback volume is remembered.',
    ],
  },
  {
    // A PATCH RELEASE MADE ALMOST ENTIRELY OF WORDS, which is the awkward case
    // for this file: only one line here changes what the app DOES. The rest are
    // labels and sentences, and a card is read by someone deciding whether to
    // download - so the test each line still has to pass is whether a person
    // would notice.
    //
    // THE TWO LISTS ARE NOT THE SAME LENGTH, on purpose. Four of these fixes
    // only exist on the English screen (Korean text showing there, one spelling,
    // a dialog title, counts of one), and a Korean reader gets nothing from
    // being told about them line by line - so Korean carries them as one line
    // at the end. Padding either list to match the other would mean inventing a
    // line or dropping a true one.
    //
    // THE AUTHOR'S OWN WORDING for the tray line, used as given (2026-08-18):
    // the draft said the tray answers once, and theirs names what was actually
    // happening - the message kept coming. English follows theirs rather than
    // the draft, so the two sides describe the same thing.
    version: 'v2.4.1',
    ko: [
      '프리셋을 연속으로 저장했을 때 트레이 메시지가 계속 나오는 것을 방지했습니다.',
      '`표시할 파일 종류`가 `표시할 파일 형식`으로 변경되었습니다.',
      '`바탕화면 전체`가 `바탕화면 채우기`로, `제목 표시줄 타이틀`이 `제목 표시줄 텍스트`로 변경되었습니다.',
      '영문 UI의 표기와 문장을 정리했습니다.',
    ],
    en: [
      'Saving presets one after another no longer leaves the tray popping.',
      'Two help rows no longer show Korean text on the English screen.',
      'One spelling throughout: colors, minimize, grayscale.',
      'The language dialog now asks to change the language instead of saying it already changed.',
      'Counts of one read correctly.',
      'Accordion Mode is now Auto-Collapse Folders, the name its Korean row already carried.',
    ],
  },
  {
    // THE AUTHOR'S OWN WORDING, used verbatim (2026-08-17). The draft handed to
    // them was rewritten line by line into one register - plain declaratives,
    // none of the em-dash asides the draft leaned on - and that register is the
    // point rather than a preference: seven lines read as one list instead of
    // seven separate remarks. When the author hands back a list, it goes in as
    // given.
    //
    // TWO LINES FOR THE MERGE, and the first of them exists for the fear rather
    // than the feature. "즐겨찾기가 북마크로 통합" on its own reads as favourites
    // being GONE, and this card sits directly above a download button - that one
    // misreading is the most expensive thing on the page. So the carry-over is
    // on the same line as the merge, and the reorder gets its own; folding them
    // together buries the half that reassures.
    //
    // SEVEN IS THE UPPER END of what this card should carry. It is a glance, and
    // the ceiling held because line 5 absorbed a second item: the picture size
    // persisting was drafted as its own line and failed 당연함 - a reader who has
    // not used the app assumes it already did that, and saying so invites the
    // thought that it could not. Merged into the album-art line, where it reads
    // as the same subject.
    //
    // LINE 7 IS THE ONLY ONE THAT NAMES A SYMPTOM, which this file's rules allow
    // for an intermittent fix: the person who hit it knows it by "폴더가 접혀
    // 있다" and by nothing else. It stays because it frightens nobody who did not
    // hit it - nothing is lost, a view is.
    version: 'v2.4.0',
    ko: [
      '즐겨찾기가 북마크로 통합되었습니다. 기존에 저장한 항목은 그대로 유지됩니다.',
      '북마크 패널에서 항목을 드래그해 순서를 변경할 수 있습니다.',
      '프리셋에 창 모드가 저장되며, 종료할 때의 모드로 실행됩니다.',
      '전체 화면 전환 시 창 크기를 그대로 유지하는 옵션이 추가되었습니다.',
      '앨범아트에 맞춤 · 1:1 · 채우기 옵션이 추가되었으며, 선택한 크기는 재시작 후에도 유지됩니다.',
      '이미 열려 있는 폴더를 클릭하면 선택만 되고, 접기는 한 번 더 클릭해야 동작합니다.',
      '폴더가 임의로 접히거나 트리가 C: 드라이브로 초기화되던 문제를 수정했습니다.',
    ],
    en: [
      'Favorites are now merged into Bookmarks, and your saved items carry over.',
      'Drag rows in the Bookmarks panel to reorder them.',
      'Presets now store the window mode, and the app reopens in the mode it was closed in.',
      'Added an option to keep the current window size when entering full screen.',
      'Album art now supports Fit, 1:1, and Fill, and the selected size persists across restarts.',
      'Clicking an already-open folder now only selects it; a second click collapses it.',
      'Fixed folders collapsing on their own and the tree resetting to C:.',
    ],
  },
  {
    // THREE LINES, and a patch release is where the rules below are easiest to
    // keep: there is no room to pad. Each of these changes what a person can do
    // or removes something they ran into, which is the whole test.
    //
    // The help gaining a line about the three icon switches is NOT here. It
    // changes what the app explains, not what it does - the same cut the
    // renames took in the entry below.
    version: 'v2.3.1',
    ko: [
      '북마크·즐겨찾기·검색 결과로 이동할 때도 폴더 자동 접기가 적용됨',
      '트리 빈 곳 우클릭에서 프리셋 사용',
      '앱 전체화면에서 창을 화면 끝까지 넓힐 수 없던 문제 수정',
    ],
    en: [
      'Folder auto-collapse now applies to bookmark, favourite and search jumps too',
      'Presets on the tree\'s empty-space right-click menu',
      'Fixed: the window could not be widened to the screen edge in full screen',
    ],
  },
  {
    // THE AUTHOR CUT THIS LIST FROM 21 LINES TO 13 (2026-08-16), and the
    // reasons given for the cuts are worth more than the list itself, because
    // they name a test this file did not have. Three words did all the work:
    // 당연함, 미약함, 사소함.
    //
    // 당연함 IS THE SHARPEST OF THE THREE - a line describing something the
    // reader assumed the app already did. It costs a slot and returns nothing,
    // and worse, it invites the thought that the app could not do it until now.
    // Four lines went out on it: the clock's size ("크기도 조절"), slideshows
    // filling the panel, films cropping to fill it, and the full cover reaching
    // its own controls. All four were genuine work; none of them is news to
    // someone who has not used the app. Ask of every line: would a reader be
    // surprised this needed saying?
    //
    // 미약함 took the 8pt font step, which had LED the card - and the round's
    // theme was a smaller screen. A settings range is not a reason to download
    // anything, even carrying its consequence. Being a round's theme does not
    // qualify a line for the top of the card; being what someone WANTS does.
    //
    // 사소함 took the footer chips going bold, the missing-folder notice, and
    // three fixes narrow enough that naming them describes a fault more than a
    // remedy (the window widening once after a full cover, the panel opening
    // shorter than its list, the tree creeping upward). Two of those are
    // absorbed rather than lost: the 더 보기·접기 line now carries the drift as
    // well, and the expand-arrow line carries the gap beside it.
    //
    // WORDING, same pass: 대폭 came out of the formats line - the count is the
    // argument, an intensifier only weakens it. And two internal names were
    // replaced by what a reader would say: 전체 덮기 → 앱 전체화면, 조작 막대 →
    // 컨트롤 패널. The card is not where the app teaches its own vocabulary.
    // (English keeps "the playback controls" there instead of a literal
    // "control panel", which is Windows' own thing.)
    //
    // THE SEARCH FREEZE SHOWED THERE IS A THIRD WAY OUT, and it is worth
    // stating as a rule (author, 2026-08-16). This file's header sets up what
    // looked like a closed bind: a symptom that frightens whoever never hit it
    // stays out, and softening it into vagueness ("반응이 느려지던") is equally
    // forbidden. The freeze is the round's most valuable fix and both doors
    // were shut on it.
    //
    // The way through is to QUALIFY THE SCOPE at the front of the line - 일부,
    // 부분적으로, 특정 경우. It is not a softener: the sentence still says the
    // app stopped responding, in the reader's own words. What it removes is the
    // implication that this is what the app does, which was the frightening
    // part all along. The precedent was already in the entry below - "일부
    // 북마크 즐겨찾기 이동 시" - written by the author for exactly this reason.
    //
    // Reach for it whenever a true line would read as a general property of the
    // app rather than as a case someone ran into.
    //
    // ONE 더 보기·접기 LINE, not the two it started as. The drift someone SAW
    // and the folder selection that answers "which of these two identically
    // named lists am I opening" are one sentence to a reader, and the fix is
    // only interesting as the reason the other half works.
    //
    // NAMED FIXES ARE WELCOME HERE (author, 2026-08-16) - what is not welcome
    // is the trivia. Renames go out on that instruction: 셔플 → 셔플 반복, and
    // the app calling itself 앱 rather than 사이드바. So do the edits with no
    // gesture behind them - the colour list gaining dividers, presets gaining
    // their own heading, the separator that kept appearing at the top of a
    // menu, a settings file that cannot be written now saying so once. All
    // real work; none of it changes what a person can do, which is the line
    // between this card and the release notes.
    //
    // THE ORDER IS THE ONLY STRUCTURE THIS LIST HAS - it renders flat, with no
    // headings - so it has to be arranged rather than appended to. Three runs:
    // what is new, then the tree and its lists, then the fixes. Fixes last
    // because a card above a download button should open on what the app does,
    // and because a run of 수정 lines reads as a list of what was broken when it
    // sits at the top.
    version: 'v2.3.0',
    ko: [
      'PSD·RAW·JXL 등 패널에서 볼 수 있는 그림 형식 추가',
      '패널 위에 시계 표시(F9)',
      '프리셋을 Ctrl+1~5로 바꾸고, Ctrl+Shift+S로 덮어씀',
      '파일을 선택하면 멀티미디어 패널이 자동으로 열림(옵션)',
      '드라이브 아이콘 표시 옵션 추가',
      '색상 설정에 펼침 화살표와 하단 칩 색 추가',
      '재생중인 음악과 별개로 선택된 음악이 더 쉽게 구분됨',
      '더 보기·접기 시 부모폴더 선택, 들여쓰기 안내선 바로 적용',
      '펼침 기호 수정 및 들여쓰기 안내선을 중심에 맞춤',
      '북마크·즐겨찾기를 추가하면 패널이 그 목록으로 바뀜',
      '일부 대용량·네트워크 폴더에서 인덱싱 도중 검색어를 고칠 때 멈추던 문제 수정',
      '썸네일 바가 PSD·RAW처럼 큰 파일까지 미리 읽어 느려지던 문제 수정',
      '음악 재생 중 앱 전체화면에서 컨트롤 패널이 가리키는 동안 사라지던 문제 수정',
    ],
    en: [
      'PSD, RAW, JXL and more picture formats in the panel',
      'A clock over the panel (F9)',
      'Switch presets with Ctrl+1-5, overwrite with Ctrl+Shift+S',
      'The multimedia panel opens when you select a file (option)',
      'An option to show drive icons',
      'Colour settings for the expand arrow and the footer chips',
      'The track you picked reads apart from the one that is playing',
      'Show more / Show less selects the parent folder, and the indent guides follow at once',
      'A reworked expand arrow, with the indent guide through its centre',
      'Adding a bookmark or favourite opens the list it went into',
      'Fixed: in some large or network folders, editing the search box while it indexed left the app unresponsive',
      'Fixed: the thumbnail bar read ahead into large files like PSD and RAW',
      'Fixed: in full screen, the playback controls hid themselves while being pointed at',
    ],
  },
  {
    // THE SWAP LEADS THE CARD, the landing fix leads the release NOTES - the
    // author's own ordering, and the two are doing different jobs. A glance
    // above a download button opens with what the app can now DO; the notes
    // below it open with the fault that took four rounds to close.
    //
    // That fix is named by the SYMPTOM for the reason this file already states -
    // whoever hit it knows it as "the tree was at the bottom", not as a
    // calculation - and it says SOME jumps, which is the truth: it depended on
    // what the panel was still holding from earlier, so plenty of jumps always
    // landed correctly.
    //
    // The next line carries its CONDITION rather than the bare symptom. Said
    // plainly it read as the app folding itself shut at random, which is the
    // shape this file warns about; with the situation attached it is
    // recognisable to whoever saw it and unalarming to whoever did not. What
    // came out with it: the selection moving to a drive, which was the frightening
    // half and is only a consequence of the fold.
    //
    // The right-dock resize wobble is NOT here, and that is the author's own
    // call: it is better, not gone, and a card sitting above a download button
    // is the wrong place to claim a fix someone can still see happening.
    version: 'v2.2.0',
    ko: [
      '멀티미디어 패널과 트리 위치를 서로 바꿀 수 있음(옵션)',
      '일부 북마크 즐겨찾기 이동 시 트리 맨 아래에 붙던 현상 수정',
      '멀티미디어 패널을 열고 창 크기를 조절할 때 트리가 저절로 접히던 것',
      '자름맞춤 — 그림을 패널에 꽉 채워 보기',
      '그림을 볼 때 Ctrl·Shift+휠로 정밀 확대·축소',
      '폴더를 우클릭해 그 안의 음악·영상 이어 재생',
      '경로 표시줄에 방문한 폴더 목록',
      '앱 크기를 더 작게 축소할 수 있음',
      '색상 설정에 체인·모노, 목록 끝 그림자 적용',
    ],
    en: [
      'Swap the multimedia panel and the tree (option)',
      'Fixed: some jumps to a bookmark or favourite left the tree at the bottom',
      'The tree folding itself shut while the window was resized',
      'Fill — crop a picture to the panel',
      'Fine zoom on a picture with Ctrl or Shift and the wheel',
      'Right-click a folder to play the music and video in it',
      'The folders you have been in, listed on the path bar',
      'The app can be made smaller',
      'Colour chains and a greyscale roll, and shading at the ends of a list',
    ],
  },
  {
    // FIXES GET NAMED FROM HERE ON (author's call, 2026-08-14). Every entry
    // above closes with "버그 수정 및 안정성 개선", which says a round happened
    // and nothing about it. What changed the mind was the reply thread on the
    // community post: people answered warmly to being told what specifically
    // had been fixed, and the app is long past the launch weeks where a tidy
    // headline mattered more than a record. Naming a fix is not the app
    // talking itself down - it is the one line that shows someone is still
    // holding the thing.
    //
    // Which still leaves the card a GLANCE - one line each, and only for what
    // a person could have hit. Anything invisible from outside stays out and
    // lives in the release notes.
    //
    // THE LAST TWO NAME THE PROBLEM, not the working state, and that is the
    // author's own edit - they wrote both lines. A fix worth listing is worth
    // being recognised by the person who hit it, and "여러 개 선택 후 하나를
    // 해제할 때 간혹 발생하던 전체 해제" is a sentence that finds them where
    // "나머지 선택 유지" does not. Note it is the shape used for the two
    // INTERMITTENT ones: the reliable fix above them still reads as what now
    // works, because nobody needs help recognising a click that never worked.
    //
    // THE AUTO-HIDE LINES CAME OUT, also the author's edit. They were the two
    // that read as "this app can lose itself and you may not find it again" -
    // true of the version being replaced, and directly above a download
    // button. The fix ships; the sentence does not. Same call as v1.2.0's
    // 행 사라짐 - see [[release-notes-tone]] - and worth re-reading before
    // writing any card line about something going missing.
    //
    // The slideshow names WHERE it lives, because it only appears on a picture
    // with others beside it and would otherwise be a feature nobody finds.
    version: 'v2.1.0',
    ko: [
      '이미지 슬라이드 쇼(이미지 우클릭 · F8)',
      '모니터별로 다른 배경화면 지정(해당 모니터 위에서 지정할 때마다)',
      '패널 아래쪽에 반쯤 걸친 항목도 한 번에 클릭',
      '여러 개 선택 후 하나를 해제할 때 간혹 발생하던 전체 해제 문제 수정',
      '즐겨찾기·북마크로 이동한 뒤 부분적으로 해당 항목이 항상 상단으로 오지 않던 문제 수정',
    ],
    en: [
      'Image slideshow (right-click a picture, or F8)',
      'Set a different wallpaper on each monitor (whichever one the app is on)',
      'A row half-clipped at the bottom of the panel now takes one click',
      'Fixed: un-picking one of several selected rows could sometimes clear the whole selection',
      'Fixed: a favorite or bookmark did not always land at the top after the jump',
    ],
  },
  {
    // The author's own four lines, used as written. The first groups what the
    // release notes list one by one - drag to move, Shift+Delete, a folder
    // copied beside itself - under the thing they have in common, which is the
    // gesture someone already knows from Explorer. The second names where to
    // find the new item rather than describing it.
    //
    // 일치시킴, NOT 통합, and the author's own correction: "통합" reads as all of
    // Explorer's features being in here, which is a promise this app does not
    // make and does not want to be measured against. What is true is narrower -
    // the file gestures it already had now behave the way Explorer's do.
    version: 'v2.0.5',
    ko: [
      '앱의 탐색기 기능과 윈도우 탐색기 기능을 일치시킴',
      '네트워크 위치 추가 기능(빈 곳에 우클릭 메뉴)',
      '폴더 내 파일 전체 펼치기 옵션',
      '버그 수정 및 안정성 개선',
    ],
    en: [
      "The app's file operations brought in line with Windows Explorer's",
      'Add a network location (right-click the empty area)',
      'Option to show every file in a folder at once',
      'Bug fixes and stability improvements',
    ],
  },
  {
    version: 'v2.0.4',
    ko: [
      '트리 위치 표시 보완',
      '음악 재생 플레이어 기능 정렬',
    ],
    en: [
      'Tree positioning refinements',
      "The music player's controls tidied up",
    ],
  },
  {
    // One line, and the shortcut is not named. Naming it would tell everyone
    // which gesture to be wary of, right above the download buttons, for a
    // fault that is already gone in the build those buttons hand out.
    version: 'v2.0.3',
    ko: [
      '버그 수정 — 특정 단축키 문제 해결',
    ],
    en: [
      'Bug fix — resolved an issue with a particular keyboard shortcut',
    ],
  },
  {
    // Back to three, which the two entries below both had reasons to exceed.
    // The panel's rename is not one of them: it matters to someone already
    // using the app and looking for it in the options menu, and that person is
    // reading the release notes or the help, not a card on the way to a
    // download button.
    version: 'v2.0.2',
    // Options saving the moment they are clicked is NOT in these lines, and it
    // is the better-known half of this release. Saying it here would tell
    // someone who never lost a setting that settings used to be lost - a line
    // that costs more in doubt than it earns in credit, right above the
    // download buttons. It is in the README's changelog, where the reader has
    // already decided to look.
    ko: [
      '이미지·음악·영상을 더블클릭하면 앱 안에서 바로 열도록 설정할 수 있습니다',
      '현재 재생 중인 곡과 선택한 다른 곡이 구분됩니다',
      '즐겨찾기 전체 해제, 색상 설정 창 정리',
    ],
    en: [
      'Images, music and video can open in the app itself on a double-click',
      'The track that is playing and the other one you have selected are told apart',
      'Clear all favorites, a tidier colour window',
    ],
  },
  {
    // A pointer line first, the way v1.7.1 does it below. 2.0 is the release
    // that says what the app now is, and a patch landing on top of it puts that
    // list one arrow away - so this entry says where it went rather than
    // standing in front of it.
    version: 'v2.0.1',
    ko: [
      'v2.0.0에 이어진 다듬기입니다 — 2.0의 새 기능은 아래 v2.0.0 항목을 봐 주세요',
      '트리에 표시되는 위치 관련 로직을 강화했습니다',
      '드라이브 행에 드라이브 종류에 맞는 아이콘이 표시됩니다',
      '라이트 테마에서 전체화면 재생 컨트롤이 또렷하게 보입니다',
    ],
    en: [
      'Polish on top of v2.0.0 — what 2.0 added is in the v2.0.0 entry below',
      'Stronger logic for where the tree lands and what it puts on screen',
      'Drive rows carry the icon for what kind of drive they are',
      'Full-screen playback controls read clearly in the light theme',
    ],
  },
  {
    // EIGHT lines, past the three the note above asks for, and deliberately:
    // the author's call for this release. 2.0 is the version where the app
    // stopped being a tree, and the card carrying the same list as the release
    // notes was judged worth more here than the glance the shape usually aims
    // for. Read that as an exception earned by the release, not as the rule
    // moving - the next version starts from three again.
    version: 'v2.0.0',
    ko: [
      '경로 직접 입력 및 히스토리 기능(Ctrl+←, Ctrl+→) 추가',
      '이미지 뷰어(썸네일 바 및 내비게이션)',
      '영상 재생(HDR 보정 및 자막 지원)',
      // Breaks itself: the examples are a second thought, not more of the
      // first, and on one line they pushed the entry to two rows anyway. The
      // indent is non-breaking spaces because pre-line collapses ordinary ones
      // (see UpdateNotes.vue).
      '음악 재생(앱 내에서 전역 플레이어로 설정하고 다른 작업으로 이동 가능)\n   예: 이미지 뷰어 또는 파일 관리, 검색 등',
      '사용자가 정한 앱 형태 및 설정 등을 프리셋으로 저장하고 그대로 불러올 수 있는 기능(5개까지)',
      '더 다양해진 랜덤 색상 모드',
      '메모리 관리 및 성능 최적화, 버그 수정, 앱 속도 향상',
      'F1 도움말',
    ],
    en: [
      'Type a path directly, and step back and forward through where you have been (Ctrl+←, Ctrl+→)',
      'Image viewer (thumbnail bar and navigator)',
      'Video playback (HDR correction and subtitles)',
      'Music playback (set it as the app\'s player and carry on elsewhere)\n   e.g. viewing images, managing files, searching',
      'Keep the app\'s shape and settings as presets and bring them back exactly (up to five)',
      'More varied random colour modes',
      'Memory and performance work, bug fixes, a faster app',
      'F1 help',
    ],
  },
  {
    version: 'v1.7.1',
    ko: [
      'v1.7.0에 이어진 안정성 수정입니다 — 새 기능은 아래 v1.7.0 항목을 봐 주세요',
      '앱이 펼쳐지는 순간 드물게 종료될 수 있던 문제를 수정했습니다',
    ],
    en: [
      'A stability follow-up to v1.7.0 — see below for what that release added',
      'Fixed the app closing unexpectedly in rare cases as it slides open',
    ],
  },
  {
    version: 'v1.7.0',
    ko: [
      '앱이 화면 높이를 다 쓰지 않아도 됩니다 — 위/아래 가장자리를 끌어 조정',
      '처음 설치하면 가장자리 가운데의 손잡이로 시작합니다',
      '버그 수정 및 성능 최적화',
    ],
    en: [
      'The app no longer has to fill the screen — drag its top or bottom edge',
      'A fresh install starts with the handle at the middle of the screen edge',
      'Refinements and bug fixes',
    ],
  },
  {
    // The installer is deliberately NOT one of these lines. It is the biggest
    // thing in this release, and the download cards right below already lead
    // with it - a line here would spend a quarter of the card repeating what
    // the reader is about to look at. It belongs in the release notes, which is
    // where someone arriving from the app's update mark lands.
    version: 'v1.6.0',
    ko: [
      '펼칠 때 가장자리 전체 또는 손잡이를 선택하고, 색도 지정할 수 있습니다',
      '파일 종류 필터에 원하는 확장자를 직접 넣을 수 있습니다',
      '버그 수정 및 성능 최적화',
    ],
    en: [
      'Pick what opens it back up — the whole screen edge or just a handle — and give it a colour',
      'Put your own extensions into the file type filter',
      'Refinements and bug fixes',
    ],
  },
  {
    version: 'v1.5.0',
    ko: [
      '색상 피커 — 선택하는 대로 앱에 바로 적용',
      '색상만 따로 내보내고 불러오기 — 다른 PC에서도 같은 색으로',
      '북마크 표시를 눌러 바로 해제',
      '파일 종류나 표시 개수를 바꿔도 보던 자리 그대로',
    ],
    en: [
      'A colour picker that applies to the app as you drag it',
      'Export and import the colours on their own — same palette on another PC',
      "Click a bookmark's ribbon to release it",
      'Changing the file filter or the row count keeps your place in the tree',
    ],
  },
  {
    version: 'v1.4.2',
    ko: [
      '하단 바에서 파일 종류를 눌러 걸러 보기 — 코드 · 이미지 · 문서 · 미디어',
      '글꼴 굵기 — 보통 / 굵게 / 폴더만 / 파일만',
      '검색 결과에 커서를 올리면 전체 경로 표시',
      '들여쓰기를 잘못 눌러 폴더가 접히던 문제 수정',
    ],
    en: [
      'Filter by file type from the bottom bar — code, images, documents, media',
      'Font weight — normal, bold, folders only, files only',
      'Hover a search result to see its full path',
      'Fixed folders collapsing when the indent was clicked by mistake',
    ],
  },
  {
    version: 'v1.4.1',
    ko: [
      '왼쪽 패널 표시 옵션 — 북마크 / 즐겨찾기 / 표시 안 함',
      '여러 폴더를 한 번에 선택해서 숨기기',
      '항목이 많은 메뉴를 스크롤해서 볼 수 있음',
      '폴더 복사와 북마크 이동에서 생기던 문제 수정',
    ],
    en: [
      'Side panel option — bookmarks, favorites, or hidden',
      'Hide several folders in one go',
      'Long menus scroll instead of running off the screen',
      'Fixed issues in folder copy and bookmark jumps',
    ],
  },
  {
    version: 'v1.4.0',
    ko: [
      '안 쓰는 폴더·드라이브를 트리에서 숨기기',
      '색상 설정에 색상 코드(#RRGGBB) 직접 입력',
      '즐겨찾기·북마크·검색 결과로 이동하면 대상이 맨 위로',
      '트리 행에 커서를 올리면 전체 경로 표시',
    ],
    en: [
      'Hide folders and drives you never use',
      'Type a colour code (#RRGGBB) in the colour settings',
      'Favorites, bookmarks and search results land at the top',
      'Hover a tree row to see its full path',
    ],
  },
  {
    version: 'v1.3.5',
    ko: [
      '잘라내기 추가 — Ctrl+X로 옮기기, 탐색기와 함께 사용 가능',
      '북마크를 우클릭 메뉴에서 지정·이동',
      '검색 목록이 최신이 아닐 때 알려줌',
    ],
    en: [
      'Cut added — move with Ctrl+X, works with Explorer',
      'Bookmarks set and browsed from the right-click menu',
      'The search list says when it is out of date',
    ],
  },
  {
    version: 'v1.3.3',
    ko: [
      '북마크 목록을 옵션 메뉴에서 한눈에',
      '검색 정렬을 메뉴에서 선택',
      '색상 설정·앱 정보 창이 글꼴 크기를 따라감',
    ],
    en: [
      'Every bookmark listed in the options menu',
      'Search sorting picked from a menu',
      'Color Settings and About follow the font size',
    ],
  },
  {
    version: 'v1.3.2',
    ko: [
      '네트워크 드라이브(NAS 등)가 잠들어 있어도 멈추지 않음',
      '즐겨찾기 드래그로 순서 바꾸기',
      '정렬 기준에 유형·크기 추가',
    ],
    en: [
      'A sleeping network drive no longer stalls the tree',
      'Favorites reordered by dragging',
      'Sort by type and size',
    ],
  },
]
