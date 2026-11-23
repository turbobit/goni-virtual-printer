"""기본 파일명·저장 폴더 규칙 엣지 케이스 테스트

사용법:  python -X utf8 tests/test_filenames.py
빌드된 bin/GoniPdfPrinter.exe 의 `name` 명령으로 결과를 확인한다.
"""
import os
import subprocess
import sys
import tempfile
import unicodedata

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "bin", "GoniPdfPrinter.exe")
NOW = "2026-11-23T09:05:07"


def run(*args):
    r = subprocess.run([EXE, "name", "--now", NOW, *args], capture_output=True)
    return r.returncode, r.stdout.decode("utf-8").strip()


def name(title=None, pattern=None, *extra):
    args = []
    if title is not None:
        args += ["--title", title]
    if pattern is not None:
        args += ["--pattern", pattern]
    return run(*args, *extra)[1]


# (설명, 제목, 형식, 추가 옵션, 기대값)
NAME_CASES = [
    ("Word 접미사·확장자", "분기 보고서.docx - Word", None, [], "분기 보고서.pdf"),
    ("Excel 접두사", "Microsoft Excel - 매출.xlsx", None, [], "매출.pdf"),
    ("메모장 수정 표시", "*제목 없음 - 메모장", None, [], "제목 없음.pdf"),
    ("호환 모드 표시", "보고서 [호환 모드] - Word", None, [], "보고서.pdf"),
    ("한글 문서", "계약서.hwp - 한글", None, [], "계약서.pdf"),
    ("브라우저 프로필", "뉴스 기사 - 개인 - Microsoft Edge", None, [], "뉴스 기사 - 개인.pdf"),
    ("전체 경로", "C:\\Users\\kim\\Desktop\\계약서.hwp", None, [], "계약서.pdf"),
    ("UNC 경로", "\\\\server\\share\\견적.xlsx", None, [], "견적.pdf"),
    ("URL 경로", "https://example.com/docs/guide.html", None, [], "guide.pdf"),
    ("URL 인코딩", "https://example.com/a/%EB%B3%B4%EA%B3%A0%EC%84%9C.pdf", None, [], "보고서.pdf"),
    ("URL 호스트만", "https://example.com/", None, [], "example.com.pdf"),
    ("금지 문자", 'a<b>c:d"e/f\\g|h?i*j', None, [], "a_b_c_d_e_f_g_h_i_j.pdf"),
    ("연속 금지 문자", "질문??? 답변***", None, [], "질문_ 답변_.pdf"),
    ("금지 문자만", "???", None, [], "문서.pdf"),
    ("빈 제목", "", None, [], "문서.pdf"),
    ("공백 제목", "   ", None, [], "문서.pdf"),
    ("제목 없음(인자 없음)", None, None, [], "문서.pdf"),
    ("예약 이름 CON", "CON", None, [], "_CON.pdf"),
    ("예약 이름 com1", "com1", None, [], "_com1.pdf"),
    ("예약 이름+확장자", "aux.txt", None, [], "_aux.pdf"),
    ("끝의 점·공백", "끝에 점과 공백. . .", None, [], "끝에 점과 공백.pdf"),
    ("탭·줄바꿈", "줄\n바꿈\t\t제목", None, [], "줄 바꿈 제목.pdf"),
    ("방향 제어 문자", "abc\u202Efdp.exe", None, [], "abcfdp.exe.pdf"),
    ("모르는 확장자 유지", "file.tar.gz", None, [], "file.tar.gz.pdf"),
    ("버전 번호 유지", "v1.2 릴리스", None, [], "v1.2 릴리스.pdf"),
    ("제목 속 중괄호는 전개 안 함", "{date} 메모", None, [], "{date} 메모.pdf"),
    ("날짜_제목", "분기 보고서.docx - Word", "{date}_{title}", [], "2026-11-23_분기 보고서.pdf"),
    ("개별 날짜 토큰", "x", "{yyyy}{MM}{dd}-{HH}{mm}{ss}", [], "20261123-090507.pdf"),
    ("날짜 서식", "x", "{date:yyyy년 MM월 dd일}", [], "2026년 11월 23일.pdf"),
    ("서식 속 금지 문자", "x", "{date:yyyy/MM/dd}", [], "2026_11_23.pdf"),
    ("datetime", "x", "{datetime}", [], "2026-11-23_090507.pdf"),
    ("모르는 토큰 유지", "보고서", "{unknown}_{title}", [], "{unknown}_보고서.pdf"),
    ("토큰 대소문자", "보고서", "{TITLE}", [], "보고서.pdf"),
    (".pdf 중복 방지", "보고서", "{title}.pdf", [], "보고서.pdf"),
    (".PDF 대문자", "보고서", "{title}.PDF", [], "보고서.pdf"),
    ("빈 형식 → 기본", "보고서.docx", "   ", [], "보고서.pdf"),
    ("형식 속 금지 문자", "보고서", "결재: {title}?", [], "결재_ 보고서_.pdf"),
    ("프로그램 이름 유지", "분기 보고서.docx - Word", None, ["--keep-app"], "분기 보고서.docx - Word.pdf"),
]


def check(desc, got, expected):
    ok = got == expected
    print(("OK  " if ok else "FAIL") + f" {desc}" + ("" if ok else f"\n       기대: {expected!r}\n       결과: {got!r}"))
    return ok


def main():
    results = []
    for desc, title, pattern, extra, expected in NAME_CASES:
        results.append(check(desc, name(title, pattern, *extra), expected))

    # 유니코드 정규화(NFD → NFC)
    nfd = unicodedata.normalize("NFD", "한글문서")
    results.append(check("NFD 한글 → NFC", name(nfd), "한글문서.pdf"))

    # 길이 제한 (150자) / 서로게이트 쌍을 자르지 않음
    got = name("가" * 300)
    results.append(check("긴 제목 150자 제한", len(got) - 4, 150))
    got = name("😀" * 100)
    stem = got[:-4]
    results.append(check("이모지 자르기 안전", (len(stem.encode("utf-16-le")) // 2, stem == "😀" * 75), (150, True)))

    tmp = tempfile.mkdtemp(prefix="goni_name_")

    def path_of(*extra):
        return run(*extra)[1]

    # 같은 이름 처리
    open(os.path.join(tmp, "보고서.pdf"), "wb").close()
    results.append(check("번호 붙이기", path_of("--title", "보고서", "--folder", tmp), os.path.join(tmp, "보고서 (2).pdf")))
    open(os.path.join(tmp, "보고서 (2).pdf"), "wb").close()
    results.append(check("번호 붙이기 (3)", path_of("--title", "보고서", "--folder", tmp), os.path.join(tmp, "보고서 (3).pdf")))
    results.append(check("덮어쓰기", path_of("--title", "보고서", "--folder", tmp, "--conflict", "overwrite"), os.path.join(tmp, "보고서.pdf")))
    results.append(check("묻기", path_of("--title", "보고서", "--folder", tmp, "--conflict", "ask"), "EXISTS " + os.path.join(tmp, "보고서.pdf")))
    results.append(check("새 이름은 그대로", path_of("--title", "새 문서", "--folder", tmp), os.path.join(tmp, "새 문서.pdf")))

    # 일련번호: 빈 번호 자동
    for n in ("001", "002"):
        open(os.path.join(tmp, f"스캔_{n}.pdf"), "wb").close()
    results.append(check("일련번호 다음 빈 번호", path_of("--pattern", "스캔_{counter:000}", "--folder", tmp), os.path.join(tmp, "스캔_003.pdf")))
    results.append(check("일련번호 기본 서식", path_of("--pattern", "page{counter}", "--folder", tmp), os.path.join(tmp, "page1.pdf")))

    # 폴더: 환경 변수 + 날짜 토큰, 따옴표, 상대 경로, 잘못된 경로
    temp = os.environ["TEMP"]
    got = path_of("--title", "a", "--folder", "%TEMP%\\goni\\{yyyy}-{MM}")
    results.append(check("폴더 환경 변수·날짜", got.lower(), os.path.join(os.path.abspath(temp), "goni", "2026-11", "a.pdf").lower()))
    got = path_of("--title", "a", "--folder", '"' + tmp + '\\"')
    results.append(check("폴더 따옴표·끝 역슬래시", got, os.path.join(tmp, "a.pdf")))
    docs = subprocess.run(["powershell", "-NoProfile", "-Command", "[Environment]::GetFolderPath('MyDocuments')"],
                          capture_output=True, text=True).stdout.strip()
    results.append(check("상대 경로 → 문서 폴더 기준", path_of("--title", "a", "--folder", "PDF"), os.path.join(docs, "PDF", "a.pdf")))
    code, out = run("--title", "a", "--folder", "C:\\a:b")
    results.append(check("잘못된 폴더 경로", (code, out), (2, "INVALID_FOLDER")))

    # 경로 길이: 폴더가 길면 이름을 줄여 259자 이내
    long_folder = os.path.join(tmp, "f" * (240 - len(tmp)))
    got = path_of("--title", "가" * 100, "--folder", long_folder)
    results.append(check("긴 경로 259자 이내", (len(got) <= 259, got.endswith(".pdf"), got.startswith(long_folder)), (True, True, True)))

    passed = sum(1 for r in results if r)
    print(f"\n{passed}/{len(results)} 통과")
    sys.exit(0 if passed == len(results) else 1)


if __name__ == "__main__":
    main()
