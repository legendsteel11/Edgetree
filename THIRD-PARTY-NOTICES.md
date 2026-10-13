# 서드파티 라이선스 고지

## Material Icon Theme

본 프로그램의 파일/폴더 아이콘은 [Material Icon Theme](https://github.com/PKief/vscode-material-icon-theme)
(저작자: PKief, Material Extensions) 프로젝트의 SVG 아이콘을 PNG로 변환하여 사용합니다.

```
The MIT License (MIT)
Copyright (c) 2025 Material Extensions

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## AvalonEdit

멀티미디어 패널의 텍스트 뷰어(TXT·MD 보기)는 [AvalonEdit](https://github.com/icsharpcode/AvalonEdit)
6.3.1로 그립니다. Markdown 강조 정의([src/Edgetree/Resources/Markdown.xshd](src/Edgetree/Resources/Markdown.xshd))는
AvalonEdit의 `MarkDownWithFontSize-Mode.xshd`를 고쳐 쓴 것입니다. 패키지 저작권 표기는
"2000-2025 AlphaSierraPapa for the SharpDevelop Team"입니다.

```
MIT License

Copyright (c) AvalonEdit Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

**위의 두 MIT 고지는 배포물에도 들어갑니다.** 실행 파일 안에 담겨 있고, 앱 정보 창의
"라이선스 고지 보기"로 열 수 있습니다. 저장소에서는
[src/Edgetree/Resources/THIRD-PARTY-NOTICES.txt](src/Edgetree/Resources/THIRD-PARTY-NOTICES.txt)에
있습니다.

## Material Symbols (Google)

화면 글리프 일부는 [Material Symbols](https://github.com/google/material-design-icons)
(Copyright Google LLC)에서 가져왔습니다 — 트리 행의 북마크 리본, 제목줄의 고정(keep)
아이콘, 정렬 버튼의 세 글리프(`sort`, `vertical_align_top`, `vertical_align_bottom`).

**변경 사항(Apache-2.0 §4b)**: 각 글리프의 SVG 경로를 WPF Path 지오메트리로 변환해 행의
색을 따라가고 폰트 배율에 맞춰 그려지도록 했습니다. 모양 자체는 바꾸지 않았습니다.

Apache License 2.0으로 배포됩니다. **§4a가 요구하는 라이선스 전문 사본은 배포물에 함께
들어갑니다** — 실행 파일 안에 담겨 있고, **앱 정보 창의 "Apache License 2.0 전문 보기"** 로
열 수 있습니다(단일 exe라 옆에 텍스트 파일을 둘 자리가 없어 이렇게 했습니다). 저장소에서는
[src/Edgetree/Resources/APACHE-2.0.txt](src/Edgetree/Resources/APACHE-2.0.txt)에 있습니다.

## Codicons (Microsoft)

텍스트 뷰어의 바꾸기 줄에 있는 두 버튼 아이콘은 [Codicons](https://github.com/microsoft/vscode-codicons)
(Microsoft Corporation 및 기여자)의 `replace`, `replace-all`입니다.

**변경 사항**: 각 아이콘의 SVG 경로를 하나의 WPF Path 도형으로 합쳐 앱의 글자색과 크기로
그리도록 했습니다. 모양 자체는 바꾸지 않았습니다.

[Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/)
(CC BY 4.0) 라이선스로 배포됩니다. 이 출처 표시는 배포물에도 들어갑니다. 실행 파일 안의
고지 파일에 담겨 있고, 앱 정보 창의 "라이선스 고지 보기"로 열 수 있습니다.

## .NET 8 (Microsoft)

독립 실행형(standalone) 빌드에는 .NET 8 런타임이 함께 들어갑니다. .NET은 MIT 라이선스로
배포됩니다 — <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>

## 랜딩 사이트

<https://edgetree.vercel.app> 은 Vue 3, Vite, `@vercel/analytics`로 만들었습니다. 모두 MIT
라이선스입니다.

---

이 밖에 앱은 윈도우가 제공하는 셸 아이콘을 그대로 표시합니다(운영체제 제공, 별도 고지
대상 아님).
