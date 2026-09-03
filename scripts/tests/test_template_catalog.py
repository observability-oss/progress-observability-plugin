"""Exercise the shipped .NET copier, not a Python reimplementation of it."""
from __future__ import annotations

import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SKILL = ROOT / "skills/build-template-agent"
CATALOG = json.loads((SKILL / "templates.json").read_text(encoding="utf-8"))
EXCLUDED = {"bin", "obj", "__pycache__", ".git", ".vs", ".DS_Store"}


def contents(directory: Path) -> dict[str, bytes]:
    return {
        str(path.relative_to(directory)): path.read_bytes()
        for path in directory.rglob("*")
        if path.is_file()
        and not any(
            part in EXCLUDED or part == ".env" or part.startswith(".env.")
            for part in path.relative_to(directory).parts
        )
    }


class TemplateCatalogTests(unittest.TestCase):
    def run_copier(self, *arguments: str, cwd: Path, skill: Path = SKILL):
        return subprocess.run(
            ["dotnet", "run", "--file", str(skill / "scripts/copy-template.cs"),
             "--", *arguments],
            cwd=cwd, text=True, capture_output=True, timeout=120,
        )

    def test_catalog_is_complete_and_projects_are_self_contained(self):
        self.assertEqual(
            {item["id"] for item in CATALOG},
            {"release-evidence-reviewer", "docs-qa", "operations-data-analyst", "ticket-triage"},
        )
        for item in CATALOG:
            with self.subTest(template=item["id"]):
                source = SKILL / "assets" / item["id"]
                self.assertEqual(len(set(item["smokeCases"])), 3)
                self.assertEqual([item["project"]], [p.name for p in source.glob("*.csproj")])
                project = (source / item["project"]).read_text(encoding="utf-8")
                self.assertNotIn("ProjectReference", project)
                self.assertTrue((source / "wwwroot/index.html").is_file())
                self.assertTrue((source / "README.md").is_file())

    def test_list_is_read_only_and_matches_catalog(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            result = self.run_copier("--list", cwd=root)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(json.loads(result.stdout.splitlines()[-1]), CATALOG)
            self.assertEqual(list(root.iterdir()), [])

    def test_each_selected_copy_is_byte_identical_and_only_contains_that_app(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for item in CATALOG:
                with self.subTest(template=item["id"]):
                    target = root / item["id"]
                    result = self.run_copier("--template", item["id"], cwd=root)
                    self.assertEqual(result.returncode, 0, result.stderr)
                    expected = contents(SKILL / "assets" / item["id"])
                    self.assertEqual(contents(target), expected)
                    self.assertEqual(len(list(target.rglob("*.csproj"))), 1)
                    self.assertFalse((target / "templates.json").exists())
                    self.assertFalse((target / "assets").exists())
                    for path in target.rglob("*"):
                        self.assertFalse(path.name in EXCLUDED or path.name.startswith(".env"))

    def test_legacy_default_and_empty_target_are_preserved(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            result = self.run_copier(cwd=root)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertTrue((root / "release-evidence-reviewer/ReleaseEvidenceReviewer.csproj").exists())
            empty = root / "empty target with spaces"
            empty.mkdir()
            result = self.run_copier("--target", str(empty), cwd=root)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(contents(empty), contents(SKILL / "assets/release-evidence-reviewer"))

    def test_invalid_selection_and_arguments_do_not_write(self):
        invalid = (
            ("--template", "access-request-reviewer"),
            ("--template", "../../elsewhere"),
            ("--template", "/tmp/elsewhere"),
            ("--template", "DOCS-QA"),
            ("--template",),
            ("--template", "docs-qa", "--template", "ticket-triage"),
            ("--list", "--target", "unwanted"),
            ("--target", "../unwanted"),
        )
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for arguments in invalid:
                with self.subTest(arguments=arguments):
                    result = self.run_copier(*arguments, cwd=root)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertEqual(list(root.iterdir()), [])

    def test_refuses_nonempty_and_symlink_targets_without_changing_them(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            target = root / "existing"
            target.mkdir()
            sentinel = target / "keep.txt"
            sentinel.write_text("customer content", encoding="utf-8")
            link = root / "linked"
            link.symlink_to(target, target_is_directory=True)
            for destination in (target, link):
                result = self.run_copier("--template", "docs-qa", "--target", str(destination), cwd=root)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(sentinel.read_text(encoding="utf-8"), "customer content")
                self.assertEqual(list(target.iterdir()), [sentinel])

    def test_rejects_corrupt_catalog_and_missing_or_symlinked_source(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            fixture = root / "skill"
            (fixture / "scripts").mkdir(parents=True)
            shutil.copy2(SKILL / "scripts/copy-template.cs", fixture / "scripts/copy-template.cs")
            catalog_path = fixture / "templates.json"
            target = root / "output"
            for invalid in ([], [None], [{**CATALOG[0], "id": "../escape"}],
                            [CATALOG[0], CATALOG[0]], [{**CATALOG[0], "project": "../Other.csproj"}],
                            [{**CATALOG[0], "smokeCases": ["one", "one", "two"]}]):
                catalog_path.write_text(json.dumps(invalid), encoding="utf-8")
                result = self.run_copier("--target", str(target), cwd=root, skill=fixture)
                self.assertNotEqual(result.returncode, 0)
                self.assertFalse(target.exists())
            catalog_path.write_text(json.dumps([CATALOG[0]]), encoding="utf-8")
            result = self.run_copier("--target", str(target), cwd=root, skill=fixture)
            self.assertNotEqual(result.returncode, 0)
            source = fixture / "assets" / CATALOG[0]["id"]
            source.mkdir(parents=True)
            (source / CATALOG[0]["project"]).write_text("<Project />", encoding="utf-8")
            (source / "linked").symlink_to(root / "absent")
            result = self.run_copier("--target", str(target), cwd=root, skill=fixture)
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse(target.exists())

    def test_copy_omits_secret_and_build_files_in_nested_folders(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            fixture = root / "skill"
            (fixture / "scripts").mkdir(parents=True)
            shutil.copy2(SKILL / "scripts/copy-template.cs", fixture / "scripts/copy-template.cs")
            (fixture / "templates.json").write_text(json.dumps([CATALOG[0]]), encoding="utf-8")
            source = fixture / "assets" / CATALOG[0]["id"]
            source.mkdir(parents=True)
            (source / CATALOG[0]["project"]).write_text("<Project />", encoding="utf-8")
            for relative in (".env", ".env.example", ".env.local", "bin/nested.dll",
                             "obj/cache.json", "data/.env.test", "data/.DS_Store"):
                path = source / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("synthetic exclusion marker", encoding="utf-8")
            (source / "data/keep.json").write_text("[]", encoding="utf-8")
            target = root / "output"
            result = self.run_copier("--target", str(target), cwd=root, skill=fixture)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(
                {str(path.relative_to(target)) for path in target.rglob("*") if path.is_file()},
                {CATALOG[0]["project"], "data/keep.json"},
            )


if __name__ == "__main__":
    unittest.main()
