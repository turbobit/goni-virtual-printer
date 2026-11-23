"""암호화 결과 검증 스크립트 (PyMuPDF 필요: pip install pymupdf)

사용법:  python -X utf8 tests/verify_encryption.py
빌드된 bin/GoniPdfPrinter.exe 의 encrypt 명령으로 여러 옵션 조합을 만들고,
암호 인증과 권한 비트가 의도대로인지 확인한다.
"""
import os
import subprocess
import sys
import tempfile

import fitz

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE = os.path.join(ROOT, "bin", "GoniPdfPrinter.exe")

P = {
    "print": fitz.PDF_PERM_PRINT, "print_hq": fitz.PDF_PERM_PRINT_HQ, "modify": fitz.PDF_PERM_MODIFY,
    "copy": fitz.PDF_PERM_COPY, "annotate": fitz.PDF_PERM_ANNOTATE, "form": fitz.PDF_PERM_FORM,
    "access": fitz.PDF_PERM_ACCESSIBILITY, "assemble": fitz.PDF_PERM_ASSEMBLE,
}

# (이름, CLI 옵션, 열기암호, 권한암호, 기대 암호화, 허용되어야 할 권한)
CASES = [
    ("aes256_open_owner", ["--open", "1234", "--owner", "owner!", "--print", "low", "--edit", "3"],
     "1234", "owner!", "V5 R6", {"print", "annotate", "form", "access"}),
    ("aes256_owner_only", ["--owner", "owner!", "--copy", "--print", "high"],
     "", "owner!", "V5 R6", {"print", "print_hq", "copy", "access"}),
    ("aes128_all_edit", ["--open", "abc", "--owner", "zzz", "--aes128", "--edit", "4"],
     "abc", "zzz", "V4 R4", {"modify", "annotate", "form", "access", "assemble"}),
    ("aes256_korean", ["--open", "한글암호"], "한글암호", None, "V5 R6", set(P)),
    ("aes128_nometa", ["--open", "x", "--aes128", "--no-meta"], "x", None, "V4 R4", set(P)),
]


def make_sample(path):
    d = fitz.open()
    for i in range(3):
        p = d.new_page()
        p.insert_text((72, 72), f"Page {i + 1} (paren) test", fontsize=20)
    d.set_metadata({"title": "검증 문서"})
    d.save(path, use_objstms=1, garbage=3, deflate=True)  # 객체 스트림/XRef 스트림 포함


def main():
    tmp = tempfile.mkdtemp(prefix="goni_")
    src = os.path.join(tmp, "src.pdf")
    make_sample(src)
    failed = 0
    for name, opts, user_pw, owner_pw, enc, allowed in CASES:
        out = os.path.join(tmp, name + ".pdf")
        r = subprocess.run([EXE, "encrypt", src, out, *opts], capture_output=True)
        if r.returncode != 0:
            print(f"FAIL {name}: exit {r.returncode}")
            failed += 1
            continue
        d = fitz.open(out)
        errors = []
        if user_pw:
            if not d.needs_pass:
                errors.append("열기 암호가 요구되지 않음")
            if d.authenticate("wrong-password"):
                errors.append("틀린 암호가 통과됨")
            if not d.authenticate(user_pw):
                errors.append("열기 암호 인증 실패")
        if enc not in (d.metadata.get("encryption") or ""):
            errors.append("암호화 방식 불일치: " + str(d.metadata.get("encryption")))
        got = {k for k, v in P.items() if d.permissions & v}
        if got != allowed:
            errors.append(f"권한 불일치: {sorted(got)} != {sorted(allowed)}")
        if "Page 1" not in d[0].get_text():
            errors.append("본문 복호화 실패")
        if owner_pw and fitz.open(out).authenticate(owner_pw) != 4:
            errors.append("권한(소유자) 암호 인증 실패")
        print(("OK  " if not errors else "FAIL") + f" {name}" + ("" if not errors else ": " + "; ".join(errors)))
        failed += bool(errors)

    # 이미 암호화된 파일은 거부해야 함
    r = subprocess.run([EXE, "encrypt", os.path.join(tmp, "aes128_nometa.pdf"), os.path.join(tmp, "x.pdf"), "--open", "a"])
    print(("OK  " if r.returncode == 1 else "FAIL") + " reject_already_encrypted")
    failed += r.returncode != 1

    print(f"\n{len(CASES) + 1 - failed}/{len(CASES) + 1} 통과")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
