from __future__ import annotations

import importlib.util
import os
from pathlib import Path
import stat
import tempfile
import unittest


SKILL_ROOT = Path(__file__).resolve().parents[1]
SCRIPT_PATH = SKILL_ROOT / "scripts" / "copy_template.py"
SPEC = importlib.util.spec_from_file_location("copy_template", SCRIPT_PATH)
assert SPEC is not None and SPEC.loader is not None
COPY_TEMPLATE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(COPY_TEMPLATE)


class CopyTemplateTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name).resolve()
        self.source = self.root / "asset"
        (self.source / "docs").mkdir(parents=True)
        (self.source / "app.csproj").write_bytes(b"<Project />\n")
        (self.source / "docs" / "policy.md").write_bytes(b"# Policy\n")
        executable = self.source / "run.sh"
        executable.write_bytes(b"#!/bin/sh\n")
        executable.chmod(0o755)

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_copies_fixed_tree_without_changing_bytes_or_executable_mode(self) -> None:
        target = self.root / "reviewer"

        result = COPY_TEMPLATE.copy_template(target, source=self.source)

        self.assertEqual(result, target.resolve())
        self.assertEqual((target / "app.csproj").read_bytes(), b"<Project />\n")
        self.assertEqual((target / "docs" / "policy.md").read_bytes(), b"# Policy\n")
        self.assertTrue((target / "run.sh").stat().st_mode & stat.S_IXUSR)

    def test_populates_existing_empty_real_directory(self) -> None:
        target = self.root / "empty"
        target.mkdir()

        result = COPY_TEMPLATE.copy_template(target, source=self.source)

        self.assertEqual(result, target.resolve())
        self.assertEqual((target / "docs" / "policy.md").read_bytes(), b"# Policy\n")

    def test_refuses_existing_nonempty_target_without_changing_it(self) -> None:
        target = self.root / "nonempty"
        target.mkdir()
        (target / "keep.txt").write_text("keep", encoding="utf-8")

        with self.assertRaises(COPY_TEMPLATE.CopyRefusedError):
            COPY_TEMPLATE.copy_template(target, source=self.source)

        self.assertEqual((target / "keep.txt").read_text(), "keep")

    def test_refuses_parent_traversal(self) -> None:
        target = self.root / "nested" / ".." / "reviewer"

        with self.assertRaises(COPY_TEMPLATE.CopyRefusedError):
            COPY_TEMPLATE.copy_template(target, source=self.source)

    def test_refuses_dangling_symlink_target(self) -> None:
        if not hasattr(os, "symlink"):
            self.skipTest("symlinks are unavailable")
        target = self.root / "reviewer"
        target.symlink_to(self.root / "missing")

        with self.assertRaises(COPY_TEMPLATE.CopyRefusedError):
            COPY_TEMPLATE.copy_template(target, source=self.source)

    def test_refuses_existing_symlink_target(self) -> None:
        if not hasattr(os, "symlink"):
            self.skipTest("symlinks are unavailable")
        real_target = self.root / "real-target"
        real_target.mkdir()
        target = self.root / "reviewer"
        target.symlink_to(real_target, target_is_directory=True)

        with self.assertRaises(COPY_TEMPLATE.CopyRefusedError):
            COPY_TEMPLATE.copy_template(target, source=self.source)

        self.assertEqual(list(real_target.iterdir()), [])

    def test_resolves_symlinked_parent_and_copies_to_real_path(self) -> None:
        if not hasattr(os, "symlink"):
            self.skipTest("symlinks are unavailable")
        real_parent = self.root / "real"
        (real_parent / "nested").mkdir(parents=True)
        linked_parent = self.root / "linked"
        linked_parent.symlink_to(real_parent, target_is_directory=True)

        result = COPY_TEMPLATE.copy_template(
            linked_parent / "nested" / "reviewer", source=self.source
        )

        real_target = real_parent / "nested" / "reviewer"
        self.assertEqual(result, real_target)
        self.assertEqual((real_target / "app.csproj").read_bytes(), b"<Project />\n")

    def test_excludes_transient_and_secret_local_files(self) -> None:
        for directory in ("bin", "obj", "__pycache__", ".git", ".vs"):
            transient = self.source / "nested" / directory
            transient.mkdir(parents=True)
            (transient / "local.txt").write_text("local", encoding="utf-8")

        for name in (".DS_Store", ".env", ".env.local", ".env.production"):
            (self.source / name).write_text("secret-or-local", encoding="utf-8")
        (self.source / ".env.example").write_text("SAFE=placeholder\n", encoding="utf-8")
        target = self.root / "reviewer"

        COPY_TEMPLATE.copy_template(target, source=self.source)

        for directory in ("bin", "obj", "__pycache__", ".git", ".vs"):
            self.assertFalse((target / "nested" / directory).exists())
        for name in (".DS_Store", ".env", ".env.local", ".env.production"):
            self.assertFalse((target / name).exists())
        self.assertEqual(
            (target / ".env.example").read_text(encoding="utf-8"),
            "SAFE=placeholder\n",
        )

    def test_refuses_symlink_inside_asset(self) -> None:
        if not hasattr(os, "symlink"):
            self.skipTest("symlinks are unavailable")
        (self.source / "linked-policy.md").symlink_to(
            self.source / "docs" / "policy.md"
        )

        with self.assertRaises(COPY_TEMPLATE.CopyRefusedError):
            COPY_TEMPLATE.copy_template(
                self.root / "reviewer", source=self.source
            )


if __name__ == "__main__":
    unittest.main()
