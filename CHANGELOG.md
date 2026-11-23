# 변경 이력

## [1.0.0] - 2026-11-23

첫 공개 버전.

### 추가
- 모든 Windows 프로그램에서 사용할 수 있는 가상 프린터 **Goni PDF**
  (Windows 내장 Microsoft Print To PDF 드라이버 + 명명된 파이프 로컬 포트)
- 인쇄 시 보안 설정 창: 열기 암호, 권한 암호, 인쇄·편집 허용 범위, 복사·접근성 허용
- PDF 암호화 직접 구현: AES-256 (V5 R6, 한글 암호 지원), AES-128 (V4 R4)
- 마지막 설정 기억 (암호는 Windows DPAPI로 보호), 창 없이 바로 저장 모드
- 트레이 앱: 기본 보안 설정, 저장 폴더 열기, 저장 완료 알림
- 명령줄 `encrypt` 모드
- NSIS 설치 프로그램 (앱 및 기능 등록, 무인 설치 `/S`)
- 사용 설명서, 설계 문서, 암호화 검증 스크립트

[1.0.0]: https://github.com/turbobit/goni-virtual-printer/releases/tag/v1.0.0
