// 나가는 글에 같은 실수가 다시 생겼는지 기계가 본다.  node tools/check-text.mjs
//
// 왜 있는가: 2026-08-18에 네 가지가 한 판에서 새어 나갔고 네 번 다 사용자가
// 발견했다 - 정해졌다고 말해 놓고 안 넣은 라벨, 영문 릴리즈 노트에 섞인 한글,
// 옛 이름이 남은 문서, 버전이 뒤처진 디버그 빌드. 그때 검사 스크립트를 네 개
// 짰다가 임시 폴더째로 버렸다. 버리지 않고 여기 둔다.
//
// 이 파일의 값어치는 검사 항목이 아니라 RETIRED 목록에 있다. 이름을 바꿀
// 때마다 옛 이름을 거기 한 줄 넣으면, 그 이름이 어디서 되살아나든 걸린다.
// 사람이 기억할 필요가 없어지는 것이 요점이다.
//
// 오탐이 나면 그 자리에서 고칠 것. 양치기 검사기는 아무도 안 본다.

import fs from 'node:fs'
import path from 'node:path'

const ROOT = path.resolve(import.meta.dirname, '..')
const R = (p) => path.join(ROOT, p)
const read = (p) => fs.readFileSync(R(p), 'utf8')
const HANGUL = /[가-힣]/

let failed = 0
const report = (name, hits, note) => {
  if (!hits.length) { console.log('  OK    ' + name); return }
  failed++
  console.log('  걸림  ' + name + '  (' + hits.length + '건)' + (note ? '  - ' + note : ''))
  hits.slice(0, 12).forEach((h) => console.log('          ' + h))
  if (hits.length > 12) console.log('          ... 그 밖 ' + (hits.length - 12) + '건')
}

// ---------------------------------------------------------------- 은퇴한 이름
//
// 바뀐 이름이 문서나 코드에 되살아나는지 본다. 변경 이력은 제외한다 - 그때의
// 기록이므로 옛 이름이 있는 것이 맞다.
//
// 이름을 바꿀 때 여기 한 줄 추가하는 것이 이 스크립트를 쓰는 방법이다.
const RETIRED = [
  // Shortened before it ever shipped; listed so the long form cannot linger in
  // the help or the docs.
  ['썸네일 아래 파일 이름 표시', '썸네일 파일명 표시', '2026-09-28'],
  // 라벨이 주어를 맞히려다 계속 틀렸다. 폴더 하나 · 드라이브 · 여러 줄 세 가지에
  // 걸리는 동작인데 이름은 하나만 말할 수 있어서, 화면에 뜬 쪽이 나머지 둘에는
  // 거짓이 됐다. 복수형 문자열까지 따로 두고도 안 맞았다. 옆의 잘라내기·삭제처럼
  // 주어를 빼면 어느 경우에도 틀리지 않는다.
  ['이 폴더 숨기기', '숨기기', '2026-08-25'],
  ['선택한 폴더 숨기기', '숨기기', '2026-08-25'],
  ['Hide This Folder', 'Hide', '2026-08-25'],
  ['Hide Selected Folders', 'Hide', '2026-08-25'],
  // 무엇을 한 번에 표시한다는 것인지가 이름에 없었다. 영문은 처음부터
  // Items per Folder로 대상과 범위를 다 말하고 있었고 국문만 둘 다 잃은 것이다.
  // 파일이 아니라 항목인 이유: 상한은 하위 폴더까지 함께 센다.
  ['한 번에 표시할 개수', '폴더에 표시할 항목 개수', '2026-08-24'],
  // 앱의 표기는 전체다 - 전체 해제·전체 선택·전체 표시·폴더 전체 재생·전체화면.
  // 접기만 모두/모든을 쓰고 있었다.
  ['모두 접기', '전체 접기', '2026-08-24'],
  ['모든 펼친 폴더 접기', '폴더 전체 접기', '2026-08-24'],
  ['전역 정렬 따르기', '부모 폴더 따르기', '2026-08-23'],
  ['Follow Default Sort', 'Follow Parent Folder', '2026-08-23'],
  ['전역 설정 따름', '상위 정렬 따름', '2026-08-23'],
  ['자름맞춤', '채우기', '2026-08-17'],
  ['이미지 뷰어', '멀티미디어 패널', '2026-08-12'],
  ['필름스트립', '썸네일 바', '2026-08-14'],
  ['전체 덮기', '앱 전체화면', '2026-08-16'],
  // 화면에 나오는 이름만 바뀐 것이고, 코드와 주석은 개념 이름으로 앱 전체화면을
  // 계속 쓴다 - 바탕화면까지 덮는 쪽과 구분해야 하는 자리가 있다.
  ['전체화면 보기', '전체화면', '2026-08-19'],
  ['조작 막대', '컨트롤 패널', '2026-08-16'],
  ['표시할 파일 종류', '표시할 파일 형식', '2026-08-18'],
  ['바탕화면 전체', '바탕화면 채우기', '2026-08-18'],
  ['제목 표시줄 타이틀', '제목 표시줄 텍스트', '2026-08-18'],
  ['Accordion Mode', 'Auto-Collapse Folders', '2026-08-18'],
  ['꺽쇠', '펼침기호', '2026-08-18'],
  ['아코디언', '폴더 자동 접기', '2026-08-18'],
  ['탭 간격', '들여쓰기 간격', '2026-08-18'],
  // 이름 변경이 아니라 기능이 합쳐진 것이라 더 크다. 목록이 하나가 된 뒤로
  // 화면에 즐겨찾기라는 말은 없다. 코드 주석과 변경 이력에는 남아 있는 것이
  // 맞고, 둘 다 이 검사에서 빠진다.
  ['즐겨찾기', '북마크', '2026-08-17'],
  ['Favorites', 'Bookmarks', '2026-08-17'],
]

// 변경 이력 아래는 과거의 기록이므로 잘라 낸다.
function livingPart(file, marker) {
  const lines = read(file).split(/\r?\n/)
  const cut = marker ? lines.findIndex((l) => l.startsWith(marker)) : -1
  return (cut < 0 ? lines : lines.slice(0, cut)).map((l, i) => [i + 1, l])
}

{
  const targets = [
    ['src/Edgetree/Services/Strings.cs', null],
    ['src/Edgetree/Services/HelpContent.cs', null],
    ['README.md', '## Changelog'],
    ['README-ko.md', '## 변경 이력'],
    ['landing/src/i18n.ts', null],
  ]
  const hits = []
  for (const [file, marker] of targets) {
    for (const [ln, line] of livingPart(file, marker)) {
      if (line.trim().startsWith('//')) continue // 주석은 옛 이름을 적어 두는 자리다
      for (const [old, now, when] of RETIRED) {
        if (line.includes(old)) hits.push(file + ':' + ln + '  "' + old + '" -> "' + now + '" (' + when + ')')
      }
    }
  }
  report('은퇴한 이름이 되살아났는가', hits, '변경 이력과 주석은 제외')
}

// ------------------------------------------------------------ 영문에 한글
//
// 영문 화면에 한글이 찍히던 것이 v2.4.1에서 고친 것 중 하나였다. 다시 생기면
// 여기서 걸린다. 일부러 두 언어에 같게 두는 둘은 통과시킨다.
const BILINGUAL_ON_PURPOSE = ['MenuLanguage', 'LanguageRestartNote']
{
  const src = read('src/Edgetree/Services/Strings.cs').split(/\r?\n/)
  const enStart = src.findIndex((l) => /^\s*IsEnglish = true;/.test(l))
  const hits = []
  if (enStart < 0) {
    hits.push('영문 구역을 못 찾음 - Strings.cs 구조가 바뀌었는지 확인할 것')
  } else {
    src.slice(enStart).forEach((line, i) => {
      if (line.trim().startsWith('//')) return
      if (!HANGUL.test(line)) return
      if (BILINGUAL_ON_PURPOSE.some((k) => line.includes(k))) return
      hits.push('Strings.cs:' + (enStart + i + 1) + '  ' + line.trim().slice(0, 70))
    })
  }
  report('영문 문자열에 한글', hits)
}
{
  // R(ko, ko, en, en) / T(ko, en) 의 영문 인자만 본다. 소스를 정규식으로 훑을 때
  // 가장 놓치기 쉬운 자리라 일부러 여기 둔다 - 실제로 두 줄이 이렇게 새어 나갔다.
  //
  // 인자 목록을 정규식으로 자르면 여러 줄짜리 호출에서 다음 호출까지 딸려온다
  // (처음에 그렇게 짰다가 오탐 9건이 났다). 괄호를 세어 호출 하나만 떼어낸다.
  const s = read('src/Edgetree/Services/HelpContent.cs')
  const hits = []
  const bodies = (name) => {
    const out = []
    const re = new RegExp('\\b' + name + '\\(', 'g')
    let m
    while ((m = re.exec(s))) {
      let i = m.index + m[0].length, depth = 1, inStr = false
      const start = i
      while (i < s.length && depth > 0) {
        const c = s[i]
        if (inStr) {
          if (c === '\\') i++
          else if (c === '"') inStr = false
        } else if (c === '"') inStr = true
        else if (c === '(') depth++
        else if (c === ')') depth--
        i++
      }
      out.push(s.slice(start, i - 1))
    }
    return out
  }
  const STR = /"((?:[^"\\]|\\.)*)"/g
  for (const [name, from, to] of [['R', 2, 4], ['T', 1, 2]]) {
    for (const body of bodies(name)) {
      const args = [...body.matchAll(STR)].map((x) => x[1])
      if (args.length < to) continue
      for (const a of args.slice(from, to)) {
        if (HANGUL.test(a)) hits.push('HelpContent.cs  ' + name + '(...)  영문 인자: ' + a.slice(0, 60))
      }
    }
  }
  report('F1 영문 인자에 한글', hits)
}
{
  const ts = read('landing/src/i18n.ts')
  const i = ts.indexOf('\n  en: {')
  const hits = []
  if (i < 0) hits.push('랜딩 en 블록을 못 찾음')
  else ts.slice(i).split(/\r?\n/).forEach((line) => {
    if (line.trim().startsWith('//')) return
    if (HANGUL.test(line)) hits.push('i18n.ts(en)  ' + line.trim().slice(0, 70))
  })
  report('랜딩 영문에 한글', hits)
}

// ------------------------------------------------------------ 영국식 철자
//
// 앱은 미국식으로 통일돼 있다 - 창 제목이 Color Settings 이고 파일이
// edgetree-colors.json 이라 그쪽이 기존 결정이다. 주석은 대상이 아니다.
const BRIT = /\b(colours?|coloured|behaviour\w*|favourite\w*|minimis\w+|maximis\w+|customis\w+|organis\w+|recognis\w+|licence|defence|centre\w*|greyscale|grey|cancelled|labelled|travelling|programme|catalogue)\b/gi
{
  const hits = []
  // trim: 이 파일만의 "지금 쓰고 있는 부분" 을 marker 대신 정하는 방법.
  const scan = (file, marker, trim) => {
    let part = livingPart(file, marker)
    if (trim) {
      const kept = trim(part.map(([, l]) => l))
      part = part.slice(0, kept.length)
    }
    for (const [ln, line] of part) {
      if (line.trim().startsWith('//')) continue
      if (/Apache Licen[cs]e|MIT Licen[cs]e/i.test(line)) continue
      // .cs 는 문자열 리터럴 안만, .md 는 줄 전체
      const text = file.endsWith('.cs')
        ? [...line.matchAll(/"((?:[^"\\]|\\.)*)"/g)].map((m) => m[1]).join(' ')
        : line
      for (const m of text.matchAll(BRIT)) hits.push(file + ':' + ln + '  ' + m[0])
    }
  }
  scan('src/Edgetree/Services/Strings.cs', null)
  scan('src/Edgetree/Services/HelpContent.cs', null)
  scan('README.md', '## Changelog')
  // 랜딩의 업데이트 카드. 2026-08-27 에 `colour` 두 개가 v2.5.5 카드에 그대로
  // 실려 나갈 뻔했고 영문 검수자가 잡았다 - 이 검사가 도는 다섯 파일 어디에도
  // 이 파일이 없었기 때문이다. 나가는 영문인데 안 보고 있었다.
  //
  // 가장 새 항목까지만 본다. 이미 발행된 카드는 그때의 기록이라 README 의 변경
  // 이력과 같은 이유로 손대지 않으며, 실제로 옛 항목들에 영국식 철자가 열일곱
  // 개 있다. 두 번째 `version:` 줄에서 자르는 것이 "이번 판에 쓰고 있는 항목"의
  // 경계다.
  scan('landing/src/changelog.ts', null, (lines) => {
    const versions = lines.reduce((at, l, i) => (/^\s*version: '/.test(l) ? [...at, i] : at), [])
    return versions.length > 1 ? lines.slice(0, versions[1]) : lines
  })
  scan('landing/src/i18n.ts', null)
  report('영국식 철자 (나가는 글)', hits, '발행된 변경 이력·주석 제외')
}

// -------------------------------------------------- 앱 밖에 박힌 사용자 텍스트
//
// 사용자에게 보이는 글은 Strings.cs 와 HelpContent.cs 에만 있어야 한다.
// 2026-08-18에 전수 확인한 결과이고, 새로 생기면 검수 범위가 조용히 넓어진다.
//
// 속성 이름 앞의 공백을 요구하는 것은 SizeToContent="Height" 와
// InputGestureText="Space" 가 Content=/Text= 로 걸리기 때문이다.
{
  const KEY_NAME = /^(Space|Home|End|Insert|Tab|PgUp|PgDn|Backspace)$/
  const ALLOWED = /한국어|English|Ctrl|Alt|Shift|Del|Enter|Esc|Edgetree|TabStick|SweepCap|DeskNoise|MagicLoupe|https?:|vercel|github/
  const hits = []
  for (const f of ['src/Edgetree/MainWindow.xaml', 'src/Edgetree/AboutWindow.xaml',
                   'src/Edgetree/ColorSettingsWindow.xaml', 'src/Edgetree/HelpWindow.xaml',
                   'src/Edgetree/FilterExtensionsWindow.xaml', 'src/Edgetree/PresetNameWindow.xaml']) {
    if (!fs.existsSync(R(f))) continue
    read(f).split(/\r?\n/).forEach((line, i) => {
      if (line.includes('<!--')) return
      for (const m of line.matchAll(/\s(?:Content|Text|Header|ToolTip|Title)="([^"{][^"]*)"/g)) {
        const v = m[1].trim()
        if (!HANGUL.test(v) && !/[A-Za-z]{3}/.test(v)) continue
        if (ALLOWED.test(v) || KEY_NAME.test(v) || /^F\d\d?$/.test(v)) continue
        hits.push(path.basename(f) + ':' + (i + 1) + '  "' + v.slice(0, 50) + '"')
      }
    })
  }
  report('Strings.cs 밖에 박힌 화면 텍스트', hits)
}

// -------------------------------------------------- 메뉴 태그가 살아 있는가
//
// 2026-08-25에 이것 때문에 8일을 날렸다. 즐겨찾기를 북마크로 합치면서 XAML의
// `Tag="addFavorite"` 행이 사라졌는데, 우클릭 메뉴를 설정하는 코드는 그 태그를
// 계속 찾았다. 못 찾으면 전부 포기하고 돌아가는 검사가 앞에 있어서, 그 아래
// 설정이 한 줄도 안 돌았다 - 파일에서 아무 행도 안 흐려지고, 압축 풀기가 zip이
// 아닌 행에 뜨고, "N개 항목 선택됨"이 안 나왔다. 유일한 신호는 Release에는
// 존재하지도 않는 Debug 로그 한 줄이었다.
//
// 코드가 이름으로 찾는 메뉴 행이 XAML에 실제로 있는지 본다. 행을 지우거나
// 이름을 바꾸면 여기서 걸린다.
{
  const cs = read('src/Edgetree/MainWindow.xaml.cs')
  const xaml = read('src/Edgetree/MainWindow.xaml')
  const asked = new Set([
    ...[...cs.matchAll(/FindTaggedMenuElement<[^>]+>\([^,]+,\s*"([^"]+)"/g)].map((m) => m[1]),
    ...[...cs.matchAll(/FindMenuItem\([^,]+,\s*"([^"]+)"/g)].map((m) => m[1]),
    ...[...cs.matchAll(/SetMenuItem(?:Checked|Enabled)\([^,]+,\s*"([^"]+)"/g)].map((m) => m[1]),
  ])
  const have = new Set([
    ...[...xaml.matchAll(/Tag="([^"]+)"/g)].map((m) => m[1]),
    ...[...xaml.matchAll(/AutomationProperties.AutomationId="([^"]+)"/g)].map((m) => m[1]),
    ...[...xaml.matchAll(/x:Name="([^"]+)"/g)].map((m) => m[1]),
  ])
  const hits = [...asked].filter((tag) => !have.has(tag))
    .map((tag) => tag + ' - 코드가 찾는데 XAML에 없음')

  // 이 검사가 스스로 고장난 채로 통과하지 않게 하는 줄이다. 위의 정규식은
  // 처음 커밋될 때 역슬래시가 빠진 채로 들어갔고(bd4f901), 그래서 아무것도
  // 안 찾으면서 계속 OK 를 찍고 있었다 - 막으려던 그 고장과 같은 모양이다.
  // 조회가 하나도 안 잡히면 그건 코드에 조회가 없어진 것이 아니라 여기가
  // 깨진 것이다.
  if (asked.size < 20) hits.push('코드에서 태그 조회를 ' + asked.size + '개밖에 못 찾음 - 위 정규식을 볼 것')

  report('메뉴 태그가 XAML에 있는가', hits, asked.size + '개 확인')
}

// ------------------------------------------- 색상 줄이 여덟 군데를 다 거쳤는가
//
// 2026-08-26. 색상 설정에 줄 하나를 넣는 데 여덟 군데가 그 줄을 알아야 하는데,
// 그중 둘을 빠뜨렸다 - ColorBindingFor(스와치가 값에 닿는 유일한 통로)와
// ColorSwatches(묶기가 도는 목록). 증상은 오류가 아니라 침묵이었다: 헥스 상자가
// 비어 있고, 색을 골라도 스와치만 잠깐 물들고 아무것도 저장되지 않았다.
//
// XAML 의 스와치 하나하나가 코드의 그 두 목록에 들어 있는지 본다. 새 줄을 넣고
// 하나라도 빠뜨리면 여기서 걸린다.
{
  const cs = read('src/Edgetree/ColorSettingsWindow.xaml.cs')
  const xaml = read('src/Edgetree/ColorSettingsWindow.xaml')
  const swatches = [...xaml.matchAll(/x:Name="(\w+Swatch)"/g)].map((m) => m[1])
  const bound = new Set([...cs.matchAll(/ReferenceEquals\(swatch, (\w+Swatch)\)/g)].map((m) => m[1]))
  const listed = new Set(
    (cs.match(/private Border\[\] ColorSwatches[\s\S]*?};/)?.[0] ?? '')
      .match(/\w+Swatch/g) ?? [])
  const hits = []
  for (const name of swatches) {
    if (!bound.has(name)) hits.push(name + ' - ColorBindingFor 에 없음 (헥스 상자가 비고 고른 색이 저장되지 않는다)')
    if (!listed.has(name)) hits.push(name + ' - ColorSwatches 에 없음 (묶기가 이 줄을 건너뛴다)')
  }
  if (swatches.length < 20) hits.push('XAML 에서 스와치를 ' + swatches.length + '개밖에 못 찾음 - 위 정규식을 볼 것')
  report('색상 줄이 코드의 두 목록에 다 있는가', hits, swatches.length + '줄 확인')

}

// ------------------------------------------------------------ Grid.Row 넘침
//
// 줄을 하나 끼우면 Grid.Row 를 전부 다시 매기고 RowDefinition 도 하나 늘려야
// 한다. 늘리는 것을 잊으면 WPF 는 아무 말도 안 하고 넘치는 것을 마지막 행에
// 넣어 버린다 - 2026-08-26 에 색상 창의 버튼 줄이 자동 숨김 손잡이 위에 겹쳐
// 그려졌다.
//
// 2026-08-29에 격자별로 보도록 다시 씀. 처음 판은 **파일 전체의 최대 Grid.Row**
// 와 **가장 큰 RowDefinitions 블록**을 견줬는데, 그건 지배적인 격자가 하나인
// 색상 창에서만 통한다. MainWindow 는 격자가 수십 개라 어딘가의 큰 블록이 다른
// 데의 큰 번호를 늘 덮어 주고, 검사가 통과만 한다.
//
// 그래서 태그를 훑으며 열린 Grid 스택을 들고 간다. Grid.Row 는 그 요소를
// 감싸는 가장 가까운 Grid 몫이고, **중첩 격자의 Grid.Row 는 자기가 아니라
// 부모 몫**이라는 것이 파일 전체 최대값 방식이 놓치던 자리다.
//
// RowDefinitions 가 아예 없는 Grid 는 암묵적으로 1행이므로 Grid.Row="1" 도 같은
// 결함이다. 스타일/템플릿의 <Setter Property="Grid.Row"> 는 문법이 달라 안 걸린다.
function gridRowOverflows(xaml, file) {
  const TAG = /<(\/?)([A-Za-z_][\w.]*)((?:[^<>"']|"[^"]*"|'[^']*')*?)(\/?)>/g
  const stack = []   // 열려 있는 Grid, 안쪽이 뒤
  const open = []    // 열려 있는 모든 요소 - Grid 가 언제 닫히는지 알기 위해
  const grids = []
  // x:Name -> the Grid object that owns the element's row, for the code-behind
  // check below. The grid object is stored by reference, so `defined` is read
  // after the walk has filled it in.
  const namedRows = new Map()
  let m
  while ((m = TAG.exec(xaml))) {
    const [, slash, name, attrs = '', selfSlash] = m
    const selfClosing = selfSlash === '/'
    if (slash === '/') {
      while (open.length) {
        const e = open.pop()
        if (e === 'Grid') stack.pop()
        if (e === name) break
      }
      continue
    }
    if (name.includes('.')) {
      if (name === 'Grid.RowDefinitions' && stack.length) {
        const end = xaml.indexOf('</Grid.RowDefinitions>', m.index)
        const block = end < 0 ? '' : xaml.slice(m.index, end)
        stack[stack.length - 1].defined = (block.match(/<RowDefinition/g) ?? []).length
      }
      if (!selfClosing) open.push(name)
      continue
    }
    const row = /\bGrid\.Row="(\d+)"/.exec(attrs)
    if (row && stack.length) {
      stack[stack.length - 1].used.push(
        { row: +row[1], line: xaml.slice(0, m.index).split('\n').length, name })
    }
    // Read before the `Grid` push below, so a nested grid's own name lands on
    // the PARENT - the same rule Grid.Row follows a few lines up.
    const named = /\bx:Name="(\w+)"/.exec(attrs)
    if (named && stack.length) namedRows.set(named[1], stack[stack.length - 1])
    if (name === 'Grid') {
      const g = { line: xaml.slice(0, m.index).split('\n').length, defined: 0, used: [] }
      stack.push(g)
      if (selfClosing) stack.pop()
      else { open.push(name); grids.push(g) }
    } else if (!selfClosing) {
      open.push(name)
    }
  }

  const hits = []
  for (const g of grids) {
    if (g.used.length === 0) continue
    const rows = g.defined === 0 ? 1 : g.defined
    const max = Math.max(...g.used.map((u) => u.row))
    if (max + 1 > rows) {
      const worst = g.used.filter((u) => u.row >= rows).map((w) => w.name + '@' + w.line)
      hits.push(file + ':' + g.line + '  행이 ' + rows + '개인데 Grid.Row 는 ' +
        max + ' 까지 씀 - ' + worst.join(', '))
    }
  }
  return { hits, count: grids.length, namedRows }
}

// ------------------------------------------------- Grid.SetRow in code-behind
//
// The XAML pass above cannot see the other half of the same defect. A row moved
// or renumbered in XAML leaves `Grid.SetRow(SomePanel, 3)` in code-behind saying
// the old number, and WPF says nothing - it drops the overflow into the last
// row. That is the shape the 2026-08-30 round paid 637MB to find, and it is why
// this half was owed.
//
// Only integer literals are checked. A variable (`Grid.SetRow(x, i)`) carries no
// number to compare, and the calls that use one are the ones that already think
// about which row they mean.
//
// One known limit: the row count comes from the element's XAML parent grid. Code
// that reparents an element into a different grid is measured against the wrong
// one, so a hit here is a question for a human, not a verdict.
function gridSetRowOverflows(cs, csFile, namedRows) {
  const hits = []
  let checked = 0
  for (const m of cs.matchAll(/Grid\.SetRow\(\s*([A-Za-z_]\w*)\s*,\s*(\d+)\s*\)/g)) {
    const g = namedRows.get(m[1])
    if (!g) continue                       // not a named element of this window
    checked++
    const rows = g.defined === 0 ? 1 : g.defined
    if (+m[2] + 1 > rows) {
      hits.push(csFile + ':' + cs.slice(0, m.index).split('\n').length +
        '  ' + m[1] + ' 의 격자는 행이 ' + rows + '개인데 Grid.SetRow 는 ' + m[2] + ' 을 씀')
    }
  }
  return { hits, checked }
}
{
  const files = ['src/Edgetree/MainWindow.xaml', 'src/Edgetree/ColorSettingsWindow.xaml',
                 'src/Edgetree/HelpWindow.xaml', 'src/Edgetree/AboutWindow.xaml',
                 'src/Edgetree/FilterExtensionsWindow.xaml', 'src/Edgetree/PresetNameWindow.xaml']
  const hits = []
  const codeHits = []
  let grids = 0
  let calls = 0
  for (const f of files) {
    if (!fs.existsSync(R(f))) continue
    const r = gridRowOverflows(read(f), f)
    hits.push(...r.hits)
    grids += r.count
    const codeFile = f + '.cs'
    if (!fs.existsSync(R(codeFile))) continue
    const c = gridSetRowOverflows(read(codeFile), codeFile, r.namedRows)
    codeHits.push(...c.hits)
    calls += c.checked
  }
  report('Grid.Row 가 정의된 행을 넘지 않는가', hits, grids + '개 격자 확인')
  report('코드 뒤 Grid.SetRow 도 정의된 행 안인가', codeHits, calls + '개 호출 확인')
}

// ------------------------------------------------------- 문자열의 중괄호와 짝
//
// 2026-08-27. 영문 검수로 100여 개 문구를 한 판에 다시 썼다. 그 작업에서 가장
// 흔한 사고는 오타가 아니라 중괄호다 - `{0}` 이 하나 빠지면 그 문구를 쓰는
// 자리가 화면에 뜨는 순간 FormatException 으로 죽고, `{` 하나가 새로 들어가도
// 마찬가지다. 컴파일러는 아무 말도 안 한다.
//
// 두 가지를 본다.
//   1. 모든 리터럴이 string.Format 이 읽을 수 있는 모양인가. `{n}` · `{n,a}` ·
//      `{n:fmt}` · `{{` · `}}` 만 허용한다.
//   2. 한국어와 영어가 같은 자리표시자를 쓰는가. 한쪽에만 `{1}` 이 있으면 그
//      언어에서만 죽으므로, 국어로 쓰고 국어로 테스트하면 영어에서 터진다.
//
// 이 검사가 그날 실제로 잡은 것은 0건이었다. 잡을 것이 없다는 것을 확인하는 데
// 스크립트를 한 번 짰으므로 버리지 않고 둔다.
{
  const src = read('src/Edgetree/Services/Strings.cs')
    .split(/\r?\n/)
    .filter((l) => !l.trim().startsWith('//'))
    .join('\n')
  const split = src.indexOf('IsEnglish = true;')

  // 이름 = "리터럴"; - 여러 줄에 걸친 것과 + 로 이어 붙인 것까지 한 덩어리로.
  const ASSIGN = /(\w+)\s*=\s*((?:@?"(?:[^"\\]|\\.)*"\s*\+?\s*)+);/g
  const LITERAL = /"((?:[^"\\]|\\.)*)"/g
  const ko = new Map(), en = new Map()

  const braceHits = []
  for (const m of src.matchAll(ASSIGN)) {
    const name = m[1]
    const parts = [...m[2].matchAll(LITERAL)].map((x) => x[1])
    const holes = new Set()
    for (const text of parts) {
      for (let i = 0; i < text.length; i++) {
        const c = text[i]
        if (c === '{') {
          if (text[i + 1] === '{') { i++; continue }
          const close = text.indexOf('}', i)
          if (close < 0) { braceHits.push(name + '  닫히지 않은 {  ' + text.slice(0, 50)); break }
          const inner = text.slice(i + 1, close)
          if (/^\d+(,-?\d+)?(:[^{}]*)?$/.test(inner)) holes.add(+inner.split(/[,:]/)[0])
          else braceHits.push(name + '  읽을 수 없는 {' + inner + '}  ' + text.slice(0, 50))
          i = close
        } else if (c === '}') {
          if (text[i + 1] === '}') { i++; continue }
          braceHits.push(name + '  짝 없는 }  ' + text.slice(0, 50))
        }
      }
    }
    // 번호에 구멍이 있으면(0 과 2 만) string.Format 이 인자를 셋 요구한다.
    const list = [...holes].sort((a, b) => a - b)
    if (list.length && (list[0] !== 0 || list[list.length - 1] !== list.length - 1)) {
      braceHits.push(name + '  자리표시자 번호가 이어지지 않음 {' + list.join(',') + '}')
    }
    ;(m.index < split ? ko : en).set(name, list.join(','))
  }

  if (ko.size < 300) braceHits.push('국어 문자열을 ' + ko.size + '개밖에 못 찾음 - 위 정규식을 볼 것')
  report('문자열의 중괄호가 성한가', braceHits, ko.size + ' + ' + en.size + '개 확인')

  const pairHits = []
  for (const [name, k] of ko) {
    const e = en.get(name)
    if (e === undefined || e === k) continue
    pairHits.push(name + '  국어 {' + k + '} / 영문 {' + e + '} - 한쪽 언어에서만 죽는다')
  }
  report('두 언어가 같은 자리표시자를 쓰는가', pairHits, en.size + '쌍 대조')
}

// ------------------------------------------------------------------ 버전
//
// 2026-08-18: 버전을 올린 뒤 Debug 를 다시 안 만들고 띄워서, 사용자가 2.4.0
// 으로 뜬다고 알려 주었다. 빌드가 csproj 보다 오래됐는지 본다.
{
  const csproj = read('src/Edgetree/Edgetree.csproj').match(/<Version>([^<]+)<\/Version>/)?.[1]
  const hits = []
  console.log('\n  csproj 버전  ' + csproj)
  for (const [label, p] of [['Debug  ', 'src/Edgetree/bin/Debug/net8.0-windows/Edgetree.dll'],
                            ['Release', 'src/Edgetree/bin/Release/net8.0-windows/Edgetree.dll']]) {
    if (!fs.existsSync(R(p))) { console.log('  ' + label + ' 빌드 없음'); continue }
    const buf = fs.readFileSync(R(p)).toString('latin1')
    const v = buf.match(/(\d+\.\d+\.\d+)\+[0-9a-f]{40}/)?.[1] ?? '(못 읽음)'
    console.log('  ' + label + ' 빌드  ' + v)
    if (v !== csproj) hits.push(label.trim() + ' 빌드가 ' + v + ' - csproj 는 ' + csproj + '. 다시 빌드할 것')
  }
  report('빌드가 csproj 버전과 같은가', hits)
}

console.log(failed === 0 ? '\n전부 통과.' : '\n' + failed + '가지 걸림.')
process.exit(failed === 0 ? 0 : 1)
