import { ref, computed, watchEffect } from 'vue'

export type Lang = 'ko' | 'en'

const LANG_KEY = 'edgetree.lang'

// WHICH LANGUAGE A VISITOR ARRIVES IN (2026-08-27, on report).
//
// It was the literal 'ko' until now, so an English speaker landed on a Korean
// page with a Korean tab title and had to find the toggle to leave it. The
// 2026-08-19 round moved the title into this dictionary so that switching
// updates the tab - which fixed the title for whoever pressed the switch, and
// left untouched the fact that nobody arrives on the other side of it.
//
// A SAVED CHOICE WINS OVER THE BROWSER, because pressing the toggle is a
// visitor saying the browser guessed wrong, and it should not have to be said
// twice. localStorage throws outright in some privacy modes rather than
// returning null, so both halves are wrapped; an unreadable store just means
// the browser decides again, which is the same answer a first visit gets.
//
// THE LIST IS ORDERED, AND THE ORDER IS THE ANSWER. Whichever of the two
// languages this page has appears FIRST wins; anything it has no words for is
// stepped over rather than counted against either.
//
// The first cut asked whether Korean appeared anywhere in the list at all, and
// that was wrong on the very machine it was written for (2026-08-27). Chrome's
// `navigator.languages` is the PREFERRED LANGUAGES list in settings, not the
// display language - so switching Chrome to English leaves 한국어 sitting in
// that list, usually below English, and "Korean is in there somewhere" sent a
// reader who had just asked for English to the Korean page.
//
// English is what is left when neither is named: a page in a language someone
// cannot read at all is worth less to them than one they can read something
// of, and Korean is the narrower of the two by far.
function initialLang(): Lang {
  try {
    const saved = localStorage.getItem(LANG_KEY)
    if (saved === 'ko' || saved === 'en') {
      return saved
    }
  } catch {
    // Unreadable store - fall through to the browser.
  }

  const tags =
    typeof navigator === 'undefined'
      ? []
      : navigator.languages && navigator.languages.length > 0
        ? navigator.languages
        : [navigator.language]

  for (const tag of tags) {
    const code = (tag ?? '').toLowerCase()
    if (code.startsWith('ko')) {
      return 'ko'
    }
    if (code.startsWith('en')) {
      return 'en'
    }
  }

  return 'en'
}

export const lang = ref<Lang>(initialLang())

export function toggleLang() {
  lang.value = lang.value === 'ko' ? 'en' : 'ko'
  try {
    localStorage.setItem(LANG_KEY, lang.value)
  } catch {
    // Not being able to remember the choice does not stop it taking effect now.
  }
}

const dict = {
  ko: {
    // The tab title, and the one thing on the page that index.html also
    // states. It is written there because a crawler reads it before any of
    // this runs; it is written HERE because that makes it static, and a
    // static Korean title stayed Korean for a visitor who had switched to
    // English (2026-08-19). Keep the two in step.
    docTitle: 'Edgetree 엣지트리 | 화면 가장자리의 Windows 폴더 탐색기',
    nav: {
      howto: '사용방법',
      screenshots: '주요기능',
      features: '기타기능',
      // AFTER 사용방법 in the bar, which is also where the section sits: the
      // whole-screen shots answer "what does that look like", and that is not a
      // question anyone has until the page has said what the app does.
      shots: '스크린샷',
      download: '다운로드',
      github: 'GitHub',
    },
    hero: {
      eyebrow: 'Windows 폴더/파일 탐색기 유틸',
      title: 'Edgetree',
      // TWO HALVES, and the comma is the join: where the app sits, and what
      // opens out of it. The multimedia panel had never been named in the hero
      // at all, which left the half no other explorer has to be found by
      // scrolling (author, 2026-08-19).
      //
      // "가벼운" came out in the same pass. It was meant as footprint and read
      // as "an explorer with less in it" - the one word on the page arguing
      // against the panel, HDR, subtitles, search and presets sitting below
      // it. The download section already carries the sizes, which is where a
      // number belongs. A superlative was raised here and refused: the
      // communities this page actually gets its visitors from discount them on
      // sight, and the measured lift on the author's previous app came from
      // removing self-criticism, never from adding boasts.
      tagline: '화면 가장자리의 폴더 탐색기, 바로 펼쳐서 감상하는 이미지·음악·영상',
      // Line one is the tree, line two is what the app takes the place of.
      // 엣지트리 is the ONLY place on the page the name is written in Hangul,
      // and it is deliberate: Korean search has no other handle on it. The
      // English page drops the name in the same slot - see the note there.
      description:
        // Broken at the sentence, not left to wrap: at the hero's width the
        // second sentence ran onto a third line and split 수월합니 / 다
        // (2026-08-06). .description is white-space: pre-line for this.
        '원하는 폴더와 파일을 선택하여 PC 전체와 네트워크까지 빠르게 탐색하세요.\n엣지트리 하나로 파일 관리와 멀티미디어 재생까지 처리하여 데스크톱 공간을 절약합니다.',
      // Korean page only - the English one leaves this empty and the badge is
      // not rendered at all.
      //
      // Every screenshot on this page is of the English UI, and the app's name
      // is English too, so a Korean visitor scrolling past them can conclude
      // it is an English-only app and leave (the author's own report,
      // 2026-08-06). This sits ABOVE the first screenshot, which is the only
      // place that answers it in time.
      langBadge: '한국어',
      // Two buttons, not three. The third build is one scroll away in the
      // download section, and a row of three equal-looking choices at the top
      // of the page asks a question before anyone knows what the app is.
      ctaDownloadSetup: '설치 버전 다운로드',
      ctaDownloadPortable: '무설치 다운로드',
      ctaGithub: 'GitHub에서 보기',
    },
    howto: {
      title: '사용방법',
      steps: [
        // First, and deliberately before docking: this is the step that makes
        // the app feel like a different one, and it is the step nobody thinks
        // to take on their own. "처음 실행 후" belongs here rather than in the
        // hero - by this point the reader is being shown how to use it.
        { title: '트리를 내 작업에 맞게 줄이기', desc: '처음 실행 후 작업에 불필요한 폴더를 감추고, 하단 바의 필터로 볼 파일 형식을 선택해 보세요. 트리가 훨씬 짧아지고, 찾는 것이 눈에 바로 들어옵니다.' },
        { title: '도킹 / 자동 숨김 / 창 모드 전환', desc: '제목 표시줄 핀 클릭 한 번으로 화면 가장자리에 얇게 숨고, 마우스를 가장자리로 가져가면 다시 펼쳐집니다. 제목 표시줄을 드래그하여 창 모드로 전환할 수 있습니다. 좌/우측 혹은 다른 모니터에 적용이 가능합니다.' },
        { title: '파일 검색 및 네트워크 경로 등록', desc: '제목 표시줄의 검색 아이콘 혹은 Ctrl + F / Ctrl + E 로 검색창과 탐색기 창을 전환하고 간단한 파일 검색을 할 수 있습니다. 또 네트워크 폴더를 등록하여 트리에서 언제든지 사용할 수 있습니다.' },
        { title: '컨텍스트 메뉴 및 파일 액세스', desc: '탐색기에서 폴더나 파일을 우클릭하여 기본적인 기능 등을 편리하게 사용할 수 있습니다. 이미지 파일은 열지 않고도 메뉴 상단의 미리보기로 바로 확인됩니다. 파일을 창 외부로 복사하거나, VS Code의 탐색기에 바로 등록할 수 있습니다.' },
        { title: '전체 접기와 펼침 상태 복원, 폴더 자동 접기', desc: '제목 표시줄의 ▲ 아이콘으로 펼쳐진 폴더 트리 전체를 한 번에 접었다 복원할 수 있습니다. 또 필요한 폴더만 펼치고 나머지는 자동으로 접히게 할 수 있습니다.' },
        { title: '멀티미디어 패널', desc: '트리 우측의 멀티미디어 패널을 펼쳐 이미지/음악/영상 등을 확인하거나 재생할 수 있습니다. 이미지의 경우 확대/축소/팬/바탕화면 지정(모니터 개별), 슬라이드쇼 기능을 지원하고 음악의 경우 작업 중 백그라운드 재생이 가능합니다. 영상은 HDR 보정과 자막을 지원합니다.' },
        { title: '경로 직접 입력 및 히스토리 기능', desc: '트리 하단의 입력칸에 경로를 직접 입력하여 빠르게 이동할 수 있습니다. 또 작업 중 사용했던 폴더로 Ctrl + ← / → 키 및 버튼을 눌러 이동이 가능합니다.' },
        { title: '설정 프리셋 기능', desc: '최대 5개의 형태로 앱의 전체 설정을 미리 저장해 놓고 필요할 때 빠르게 선택하여 사용할 수 있습니다. 예를 들어 음악 재생용일 경우는 미니 MP3 플레이어 형태로, 이미지 작업용일 경우 이미지 파일만 필터로 지정하고 멀티미디어 패널이 열린 상태로, 완전한 탐색기로 사용할 경우 최대 높이와 적절한 텍스트 크기 등을 저장해 놓을 수 있어 편리합니다.' },
      ],
    },
    screenshots: {
      title: '주요기능',
      items: [
        {
          title: '멀티미디어 패널: 이미지',
          desc: '폴더에 포함된 이미지 전체를 빠르게 넘기며 볼 수 있습니다. 확대/축소/전체화면 및 내비게이터 기능, 썸네일 실시간 크기조정이 가능하고 바로 끌어 작업할 수 있습니다. 바탕화면 지정도 가능합니다.',
        },
        {
          title: '멀티미디어 패널: 음악 · 영상',
          desc: '주요 음원을 재생합니다. 백그라운드 재생기능으로 다른 일반 작업과 병행이 가능하고 대부분의 영상을 HDR 보정 및 자막과 함께 감상할 수 있습니다.',
        },
        {
          title: '도킹 / 자동 숨김',
          desc: '화면의 좌/우에 고정시키거나 자동 숨김으로 마우스를 움직여 펴거나 감출 수 있습니다. 숨을 때 손잡이와 전체 막대 중에서 선택할 수 있고, 고정된 상태의 높이와 위치도 원하는 대로 조정됩니다.',
        },
        {
          title: '파일 형식 필터',
          desc: '하단 바에서 볼 파일 형식을 선택하면 나머지는 트리에서 빠집니다. 원하는 확장자를 직접 추가할 수도 있습니다.',
        },
        {
          title: '창 모드 전환',
          desc: '헤더를 드래그하면 자유롭게 움직이고 크기를 변경할 수 있는 일반 창이 됩니다.',
        },
        {
          title: '우클릭 메뉴',
          desc: '새 폴더, 복사/붙여넣기, 이름 바꾸기, 삭제, 터미널 열기 등을 한 곳에서. 이미지 파일은 메뉴 상단에 미리보기가 표시됩니다.',
        },
        {
          title: '옵션 메뉴',
          desc: '폴더 자동 접기, 항상 위에 표시, 트레이 아이콘, 정렬 기준, 언어 등 세세한 동작을 조절합니다.',
        },
        {
          title: '색상 커스터마이징',
          desc: '폴더명, 파일명, 배경, 선택 영역 등 색상을 원하는 대로 변경할 수 있고, 다크/라이트 모드를 각각 따로 지정할 수 있습니다. 색을 선택하는 동안 트리에 바로 반영되어 확인하면서 정할 수 있습니다.',
        },
        {
          title: '파일 검색',
          desc: 'Ctrl+F로 검색창을 열어 지정한 폴더 안의 파일을 이름으로 빠르게 찾고, 결과를 클릭하면 탐색기의 해당 위치로 바로 이동합니다.',
        },
      ],
    },
    features: {
      title: '기타기능',
      items: [
        { title: '폴더 트리', desc: 'VS Code 스타일 트리 탐색. 전체 접기와 펼침 상태 복원, 폴더 자동 접기, 폴더에 표시할 항목 개수 제한으로 큰 폴더도 빠르게 열립니다.' },
        { title: '도킹 및 창 모드', desc: '화면 좌/우 도킹과 자유 이동 창 모드를 오가고, 바깥 라인 더블클릭으로 창 크기를 자동으로 맞출 수 있습니다.' },
        { title: '자동 숨김', desc: '핀 클릭 한 번으로 화면 가장자리에 숨고, 마우스를 올리면 펼쳐집니다. 핀을 다시 누르면 고정됩니다.' },
        { title: '기본 옵션', desc: '부팅 후 자동 시작, 항상 위에 표시, 트레이로 최소화 등을 옵션으로 조절합니다.' },
        { title: '파일 관리', desc: '다른 앱으로 파일을 바로 드래그하고, 복사/붙여넣기/이름변경/속성 등을 지원하며 외부 변경사항도 실시간 반영됩니다. Ctrl+클릭·Shift+클릭으로 여러 항목을 선택해 한 번에 복사·삭제하거나 통째로 드래그할 수 있습니다.' },
        { highlight: true, title: '파일 형식 필터', desc: '하단 바에서 코드·이미지·문서·미디어 등 트리에 보일 파일 형식을 선택합니다. 원하는 확장자를 하나 또는 여러 개 직접 지정할 수도 있습니다. 검색은 필터로 걸러진 파일도 찾습니다.' },
        { highlight: true, title: '이미지 미리보기', desc: '이미지 파일을 우클릭하면 메뉴 상단에 썸네일과 형식·픽셀 크기·용량·수정 날짜가 표시됩니다. 열지 않고 확인하고, 썸네일을 클릭하면 바로 열립니다.' },
        { highlight: true, title: '북마크', desc: 'Ctrl+Alt+K로 파일이나 폴더에 표식을 달고, Ctrl+Alt+L / Ctrl+Alt+J로 표시한 곳들을 오갑니다. 행 오른쪽 끝의 표식은 앱을 다시 켜도 유지됩니다.' },
        { title: '압축 / 압축 풀기', desc: '우클릭 메뉴에서 선택한 항목을 zip으로 묶고(여러 개면 하나로), zip 파일은 같은 이름의 폴더로 풉니다. 별도 프로그램이 필요 없습니다.' },
        { highlight: true, title: '폴더 숨기기', desc: '잘 안 쓰는 폴더나 드라이브를 우클릭 한 번으로 트리에서 감춥니다. 감춘 것은 목록에 모여 있어 하나씩 또는 한 번에 복원할 수 있고, 검색은 감춘 폴더 안도 그대로 찾습니다.' },
        { title: '전체 경로 툴팁', desc: '트리 행에 커서를 올리면 전체 경로가 보입니다. 폴더가 깊어져 이름이 잘린 행도 그 자리에서 어디인지 확인할 수 있습니다.' },
        { title: '파일 검색', desc: '지정한 폴더를 인덱싱해 이름으로 바로 찾아갑니다. 인덱스는 저장되어 다음 실행 때 다시 훑지 않으므로, 네트워크 드라이브(NAS)처럼 오래 걸리는 곳도 앱을 켜자마자 검색할 수 있습니다.' },
        { title: '북마크 패널', desc: '자주 가는 폴더와 파일을 저장해 클릭 한 번으로 이동합니다. 행마다 번호가 붙고 그 번호가 곧 단축키 순서이며, 드래그로 순서를 변경할 수 있습니다. 영역 위치도 상/하로 변경할 수 있습니다.' },
        { title: '아이콘 종류', desc: '아이콘을 기본과 Windows 탐색기 중에서 선택할 수 있고, 폴더 아이콘과 파일 아이콘을 숨겨서 더 심플하게 볼 수도 있습니다.' },
        { title: '들여쓰기 간격과 행 간격', desc: '들여쓰기 간격과 행 간격을 각각 취향대로 좁히거나 넓힐 수 있습니다.' },
        { title: '정렬 기준', desc: '이름·날짜 기준 오름차순/내림차순. 폴더별로 전역 설정과 별개로 정렬을 지정하고, 언제든 해제할 수 있습니다.' },
        { title: '색상 커스터마이징', desc: '거의 모든 화면 요소의 색상을 원하는 대로 지정할 수 있고, 다크/라이트 모드를 지원합니다.' },
        { title: '글꼴 크기', desc: '옵션 메뉴 또는 Ctrl +/- 로 9~20pt 조절, Ctrl+0으로 기본값 복원. 아이콘과 행 간격도 함께 따라옵니다.' },
        { title: '디스플레이 배율 대응', desc: '윈도우 확대 배율(125%·150%·200% 등)에서 화면을 늘려 그리지 않고 그 배율로 직접 렌더링합니다. 고해상도 노트북이나 27인치 이상 4K 모니터에서 글자와 아이콘이 또렷합니다.' },
        { title: '설정 내보내기/가져오기/초기화', desc: 'JSON으로 저장·복원하고, 전체 초기화 시 레지스트리까지 깔끔하게 정리됩니다.' },
        { title: '한국어/영어 지원', desc: '옵션 메뉴에서 언제든 언어를 전환할 수 있습니다.' },
      ],
    },
    info: {
      requirementsTitle: '시스템 요구사항',
      requirements: [
        'Windows 10 / 11',
        // 세 판을 런타임이 필요한가로 가른다. 예전에는 "독립 실행형: 추가 설치
        // 없이 바로 실행"이었는데, 설치 버전이 생긴 마당에 "설치 없이"를 요구사항
        // 칸에서 말하면 어느 쪽 이야기인지 흐려진다. 여기서 답할 질문은 하나다 —
        // 내 PC에 뭘 더 깔아야 하는가.
        '설치 버전 · 무설치: 별도 런타임 필요 없음',
        '경량: .NET 8 데스크톱 런타임 필요',
      ],
      licenseTitle: '라이선스',
      licenseBody:
        'MIT 라이선스로 배포됩니다. 자유롭게 사용, 수정, 배포할 수 있으며 별도의 보증 없이 있는 그대로 제공됩니다.',
      licenseLink: 'LICENSE.md 전문 보기',
      iconNotice:
        '파일·폴더 아이콘: Material Icon Theme (MIT)\n화면 글리프: Material Symbols, Google (Apache License 2.0)',
      noticesLink: '서드파티 고지',
    },
    updates: {
      title: '업데이트 내역',
      older: '이전 버전',
      newer: '다음 버전',
    },
    download: {
      title: '다운로드',
      // 이름을 TabStick과 맞췄다(2026-08-06). 같은 사람이 만든 두 앱이고 랜딩끼리 링크가
      // 걸려 있어 나란히 보는 사람이 있는데, 한쪽은 '무설치'·'경량'이고 다른 쪽은
      // '독립 실행형'·'일반 버전'이면 같은 것인지 알 수 없다. '일반'은 그 자리에서
      // 아무것도 말해 주지 않는 낱말이기도 했다 - 런타임이 필요한 1MB짜리가 '일반'이고
      // 155MB짜리가 '독립 실행형'이라 오히려 뒤집혀 읽혔다.
      setupTitle: '설치 버전',
      setupDesc: '다운로드 후 클릭 몇 번, 시작 메뉴에 등록됩니다.',
      setupSize: '49 MB',
      portableTitle: '무설치',
      // TabStick은 '압축을 풀고 바로 실행합니다'인데 이쪽은 zip이 아니라 exe 하나다.
      portableDesc: '받아서 바로 실행합니다. 파일 하나입니다.',
      portableSize: '155 MB',
      lightTitle: '경량',
      lightDesc: '.NET 8 데스크톱 런타임이 필요합니다.',
      lightSize: '1 MB',
      recommend: '추천',
      button: '다운로드',
      // 두 앱을 함께 쓰는 사람을 위한 안내이자 TabStick 랜딩에 이미 있는 문장의 거울.
      // 런타임을 한 번 깔면 양쪽 경량 버전이 열린다는 것이 요지다.
      bothApps:
        '.NET 8 데스크톱 런타임을 한 번 설치해 두시면 경량 버전을 쓸 수 있고, 다른 앱 TabStick도 작은 파일 하나로 사용할 수 있습니다.',
      mobileTitle: 'Windows에서 쓰는 앱입니다.',
      mobileDesc: '주소를 복사해 두었다가 PC에서 열어보세요.',
      mobileCopy: '주소 복사',
      mobileCopied: '복사했습니다',
      smartscreenNote:
        '실행 파일에 정식 코드 서명 인증서가 없어 Windows에서 "알 수 없는 게시자" 경고가 뜰 수 있습니다.\n"추가 정보" → "실행"을 누르시면 정상적으로 실행됩니다.',
      virustotalNote:
        'VirusTotal 등에서 일부 백신이 휴리스틱(패턴 기반) 오탐을 표시할 수 있습니다. 서명되지 않은 소규모 개인 개발 프로그램에서 흔히 나타나는 현상이며, 소스 코드가 GitHub에 전부 공개되어 있어 언제든 직접 확인하실 수 있습니다.',
    },
    // The screenshot section - ONE string, which is the whole of the rule it
    // was built to: 말보다 화면. See the component for the line that was tried
    // under the title and cut.
    gallery: {
      title: '스크린샷',
    },
    footer: {
      contact: '요청·버그 신고',
      otherTool: '같은 개발자의 다른 도구',
      otherToolName: 'TabStick',
      otherTool2Name: 'SweepCap',
      otherTool3Name: 'DeskNoise',
      otherTool4Name: 'MagicLoupe',
      copyright: `© ${new Date().getFullYear()} Edgetree. MIT License.`,
    },
  },
  en: {
    // Shorter than the Korean one on purpose: that title is carrying search
    // terms Korean has no other handle on, and this one has the whole English
    // alphabet to be found by.
    docTitle: 'Edgetree | A folder tree at your screen edge',
    nav: {
      howto: 'How to Use',
      screenshots: 'Key Features',
      features: 'More Features',
      shots: 'Screenshots',
      download: 'Download',
      github: 'GitHub',
    },
    hero: {
      eyebrow: 'Windows Folder & File Explorer Utility',
      title: 'Edgetree',
      tagline: 'A folder tree at your screen edge. Pictures, music and video open right there.',
      // The Korean line names the app once (엣지트리) so Korean search has
      // something to hold; this one does not. The H1 two lines above already
      // says Edgetree in the same alphabet, so repeating it reads as a stutter
      // rather than as a name being introduced.
      //
      // "managing files and playing media" rather than "file management": in
      // English that noun phrase is the name of a product category, and it
      // promises dual panes and batch operations this app has never had.
      description:
        'Pick the folders and files you want, and move quickly across your PC and your network.\nOne app for managing files and playing media, so your desktop has room to work.',
      // Empty on purpose - see the Korean side. An English reader looking at
      // English screenshots has nothing to be corrected about.
      langBadge: '',
      ctaDownloadSetup: 'Download Installer',
      ctaDownloadPortable: 'Download Portable',
      ctaGithub: 'View on GitHub',
    },
    howto: {
      title: 'How to Use',
      steps: [
        { title: 'Cut the tree down to your work', desc: 'Right after the first launch, hide the folders you never touch and pick the file kinds you want from the bottom bar. The tree gets much shorter, and what you are looking for is simply there.' },
        { title: 'Dock / Auto-Hide / Window Mode', desc: 'One click on the title bar pin tucks it away to a thin sliver at the screen edge; move your mouse there to bring it back. Drag the title bar to switch into a floating window. It works on either side of the screen, and on any monitor you have.' },
        { title: 'File Search & Network Locations', desc: "The title bar's search icon (or Ctrl + F / Ctrl + E) toggles between the search box and the explorer for a quick file lookup. You can also register a network folder and reach it from the tree at any time." },
        { title: 'Context Menu & File Access', desc: 'Right-click a folder or file in the explorer for the essentials at hand — image files show a preview right at the top of the menu, no need to open them. Copy files out to another window, or send them straight to the VS Code explorer.' },
        { title: 'Collapse All / Restore Expanded & Auto-Collapse Folders', desc: 'The ▲ icon on the title bar collapses or restores the whole expanded tree at once; you can also keep only the folder you open expanded while the rest auto-collapse.' },
        { title: 'Multimedia Panel', desc: 'Open the multimedia panel on the right of the tree to view or play images, music and video. Images get zoom, pan, a desktop background you set per monitor, and a slideshow; music keeps playing in the background while you work. Video comes with HDR correction and subtitles.' },
        { title: 'Path Bar & History', desc: 'Type a path straight into the box below the tree to go there. You can also step through the folders you have already been in with Ctrl + ← / → or the two buttons.' },
        { title: 'Setting Presets', desc: "Save the app's whole setup as up to five presets and pick the one you need. A mini MP3 player for listening to music; an image-only filter with the multimedia panel already open for picture work; full height and a comfortable text size for using it as a proper explorer." },
      ],
    },
    screenshots: {
      title: 'Key Features',
      items: [
        {
          title: 'Multimedia panel — images',
          desc: 'Flip quickly through every image a folder holds. Zoom, full screen and a navigator, thumbnails you resize as you go, and drag one straight out to work with. It can set your desktop background too.',
        },
        {
          title: 'Multimedia panel — music and video',
          desc: 'Plays the common audio formats. Background playback keeps the sound going while you carry on with something else, and most video plays with HDR correction and subtitles.',
        },
        {
          title: 'Docking',
          desc: 'Anchors to the left or right edge of your screen, always ready — switch sides anytime from the options menu. Hiding leaves either a short handle or a bar down the whole edge, and you can set how tall it is when docked, and where it sits.',
        },
        {
          title: 'File type filter',
          desc: 'Pick the kinds of files you want from the bar at the bottom and the rest drop out of the tree. You can add extensions of your own, too.',
        },
        {
          title: 'Floating window mode',
          desc: 'Drag the header to undock into a normal, freely movable window.',
        },
        {
          title: 'Right-click menu',
          desc: 'New folder, copy/paste, rename, delete, open terminal, and more — all in one place. Image files show a preview at the top of the menu.',
        },
        {
          title: 'Options menu',
          desc: 'Fine-tune Auto-Collapse Folders, Always on Top, tray icon, sort order, language, and more.',
        },
        {
          title: 'Color customization',
          desc: 'Customizable colors for folder names, file names, backgrounds, selection, and more — with separate dark and light palettes. Every pick lands on the tree as you make it, so you judge a color where you will actually see it.',
        },
        {
          title: 'File search',
          desc: 'Ctrl+F opens a search box to find files by name inside a chosen folder; click a result to jump straight to it in the tree.',
        },
      ],
    },
    features: {
      title: 'More Features',
      items: [
        { title: 'Folder Tree', desc: "VS Code-style tree browsing - Collapse All and Restore Expanded, Auto-Collapse Folders, and an items-per-folder cap keep even huge folders fast." },
        { title: 'Docking & Window Mode', desc: "Dock to either screen edge or float as a free window - double-click the edge to auto-fit the width." },
        { title: 'Auto-Hide', desc: 'One click of the pin tucks it away to a thin sliver at the screen edge; hover to reveal, click it again to keep it open.' },
        { title: 'Core Options', desc: "Start with Windows, Always on Top, minimize to tray - all toggleable from the options menu." },
        { title: 'File Management', desc: 'Drag files straight into other apps, plus copy/paste/rename/properties, with live updates when files change externally. Multi-select with Ctrl+click and Shift+click to copy, delete, or drag several items at once.' },
        { highlight: true, title: 'File Type Filter', desc: 'Pick the file kinds the tree shows from the bottom bar - code, images, documents, media and so on. You can also type in your own extensions, one or several. Search still finds the files the filter is hiding.' },
        { highlight: true, title: 'Image Preview', desc: 'Right-click an image to see a thumbnail at the top of the menu with its format, pixel size, file size, and modified date - check it without opening, or click the thumbnail to open it.' },
        { highlight: true, title: 'Bookmarks', desc: 'Mark a file or folder with Ctrl+Alt+K and cycle through your marks with Ctrl+Alt+L / Ctrl+Alt+J. The marker sits at the right edge of the row and survives restarts.' },
        { title: 'Compress & Extract', desc: 'Zip the selection from the right-click menu (several items become one archive), and unpack a .zip into a folder of the same name. No extra tool needed.' },
        { highlight: true, title: 'Hide Folders', desc: 'Take a folder - or a whole drive - out of the tree with one right-click. Hidden ones collect in a list to be brought back one at a time or all at once, and search still reaches inside them.' },
        { title: 'Full Path on Hover', desc: 'Hover a tree row and its full path appears - including rows deep enough that the name itself no longer fits.' },
        { title: 'File Search', desc: 'Index a folder you choose and jump straight to a file by name. The index is saved, so it is not re-walked on the next launch - even a network drive is searchable the moment the app opens.' },
        { title: 'Bookmark Panel', desc: 'Keep the folders and files you go back to and reach them in one click. Every row is numbered, that number is its place in the shortcut cycle, and dragging a row changes it. The panel sits above or below the tree, your choice.' },
        { title: 'Icon Style', desc: "Choose between Default and the icons Windows Explorer shows, or hide folder and file icons entirely for a cleaner look." },
        { title: 'Indent & Row Spacing', desc: "Adjust the tree's indent width and row spacing independently, to taste." },
        { title: 'Sort Options', desc: "Sort by name or date, ascending or descending. Each folder can keep its own sort independent of the global default, and you can clear it anytime." },
        { title: 'Color Customization', desc: 'Recolor nearly every visible element to your liking, with separate dark and light modes.' },
        { title: 'Font Size', desc: 'Adjust 9-20pt from the options menu or with Ctrl +/-, Ctrl+0 to reset. Icons and row spacing follow along.' },
        { title: 'Display Scaling', desc: 'Renders at your display\'s actual scale (125%, 150%, 200%…) rather than being stretched to fit it, so text and icons stay sharp on high-resolution laptops and 27"+ 4K monitors.' },
        { title: 'Export / Import / Reset Settings', desc: "Save and restore settings as JSON - a full reset also cleans up the Windows registry entry." },
        { title: 'Korean / English UI', desc: 'Switch languages anytime from the options menu.' },
      ],
    },
    info: {
      requirementsTitle: 'System Requirements',
      requirements: [
        'Windows 10 / 11',
        'Installer and portable: no runtime needed',
        'Light build: requires the .NET 8 Desktop Runtime',
      ],
      licenseTitle: 'License',
      licenseBody:
        'Released under the MIT License — free to use, modify, and distribute, provided as-is without warranty.',
      licenseLink: 'Read the full LICENSE.md',
      iconNotice:
        'File and folder icons: Material Icon Theme (MIT)\nInterface glyphs: Material Symbols, Google (Apache License 2.0)',
      noticesLink: 'Third-party notices',
    },
    updates: {
      title: 'update notes',
      older: 'Older version',
      newer: 'Newer version',
    },
    download: {
      title: 'Download',
      setupTitle: 'Installer',
      setupDesc: 'Download, click through, done — it lands in your Start menu.',
      setupSize: '49 MB',
      portableTitle: 'Portable',
      portableDesc: 'One file. Run it, nothing to install.',
      portableSize: '155 MB',
      lightTitle: 'Light',
      lightDesc: 'Needs the .NET 8 Desktop Runtime.',
      lightSize: '1 MB',
      recommend: 'Recommended',
      button: 'Download',
      bothApps:
        'Install the .NET 8 Desktop Runtime once and you can use the Light build here — and TabStick, the other app, as a single small file too.',
      mobileTitle: 'This one runs on Windows.',
      mobileDesc: 'Copy the address and open it on your PC.',
      mobileCopy: 'Copy address',
      mobileCopied: 'Copied',
      smartscreenNote:
        "There's no paid code-signing certificate on the exe, so Windows may show an \"Unknown publisher\" SmartScreen warning —\nclick \"More info\" then \"Run anyway\" to proceed.",
      virustotalNote:
        'A few antivirus engines on VirusTotal and similar sites may flag it with a generic heuristic detection - common for small, unsigned indie apps. The full source is public on GitHub, so you’re welcome to check it yourself.',
    },
    gallery: {
      title: 'Screenshots',
    },
    footer: {
      contact: 'Requests & bug reports',
      otherTool: 'Other tools by the same maker',
      otherToolName: 'TabStick',
      otherTool2Name: 'SweepCap',
      otherTool3Name: 'DeskNoise',
      otherTool4Name: 'MagicLoupe',
      copyright: `© ${new Date().getFullYear()} Edgetree. MIT License.`,
    },
  },
} as const

export const t = computed(() => dict[lang.value])

// Set here rather than in a component: the language is this module's state and
// the document title is one thing, not any component's. index.html's own title
// stands until this first runs, which is what a crawler sees.
watchEffect(() => {
  document.title = t.value.docTitle
})
