from __future__ import annotations

import importlib.util
from pathlib import Path
import tempfile
import unittest


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "build_copilot.py"
SPEC = importlib.util.spec_from_file_location("build_copilot", SCRIPT_PATH)
assert SPEC is not None and SPEC.loader is not None
BUILD_COPILOT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BUILD_COPILOT)


class CopilotPayloadTests(unittest.TestCase):
    def test_custom_references_are_packaged_outside_app_code(self) -> None:
        source = SCRIPT_PATH.parents[1] / "skills/build-custom-agent"
        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "skill"
            BUILD_COPILOT.write_skill_payload(source, output)
            for reference in (source / "references").glob("*.md"):
                with self.subTest(reference=reference.name):
                    self.assertEqual(
                        reference.read_bytes(),
                        (output / "references" / reference.name).read_bytes(),
                    )
            starter = output / "assets/custom-agent-starter"
            self.assertFalse((starter / "references").exists())
            self.assertFalse((output / "tests").exists())
            for relative, contents in BUILD_COPILOT.tree_files(starter).items():
                with self.subTest(file=relative):
                    self.assertNotIn(b"try-and-refine", contents.lower())

    def test_copy_excludes_build_output_and_secret_env_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            source = root / "source"
            output = root / "output"
            source.mkdir()

            (source / "SKILL.md").write_text("workflow\n", encoding="utf-8")
            (source / ".env").write_text("SECRET=one\n", encoding="utf-8")
            (source / ".env.local").write_text("SECRET=two\n", encoding="utf-8")
            (source / ".env.example").write_text("SECRET=\n", encoding="utf-8")
            (source / "obj").mkdir()
            (source / "obj" / "build.json").write_text("{}\n", encoding="utf-8")
            (source / "tests").mkdir()
            (source / "tests" / "maintainer-only.txt").write_text(
                "not distributed\n", encoding="utf-8"
            )

            BUILD_COPILOT.write_skill_payload(source, output)

            self.assertTrue((output / "SKILL.md").is_file())
            self.assertTrue((output / ".env.example").is_file())
            self.assertFalse((output / ".env").exists())
            self.assertFalse((output / ".env.local").exists())
            self.assertFalse((output / "obj").exists())
            self.assertFalse((output / "tests").exists())
            self.assertEqual(
                BUILD_COPILOT.tree_files(output),
                BUILD_COPILOT.tree_files(source),
            )


if __name__ == "__main__":
    unittest.main()
