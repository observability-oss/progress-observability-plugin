from __future__ import annotations

import ast
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
SHARED_USER_SECRETS_ID = "Progress.AgentBuilder.Mvp"
TEMPLATE_SKILL = ROOT / "skills/build-template-agent"
TEMPLATES = json.loads((TEMPLATE_SKILL / "templates.json").read_text(encoding="utf-8"))


class BuilderConfigContractTests(unittest.TestCase):
    def test_both_starters_share_user_secrets_without_env_example(self) -> None:
        projects = [
            TEMPLATE_SKILL / "assets" / item["id"] / item["project"]
            for item in TEMPLATES
        ] + [ROOT
            / "skills/build-custom-agent/assets/custom-agent-starter"
            / "CustomAgent.csproj"]

        for project in projects:
            with self.subTest(project=project):
                self.assertIn(
                    f"<UserSecretsId>{SHARED_USER_SECRETS_ID}</UserSecretsId>",
                    project.read_text(encoding="utf-8"),
                )
                self.assertIn(
                    "AddUserSecrets<AgentMarker>(optional: true)",
                    (project.parent / "Program.cs").read_text(encoding="utf-8"),
                )

        custom_starter = projects[-1].parent
        self.assertFalse((custom_starter / ".env.example").exists())
        self.assertNotIn(
            "!.env.example",
            (custom_starter / ".gitignore").read_text(encoding="utf-8"),
        )

    def test_builders_do_not_carry_or_sync_the_mcp_contract(self) -> None:
        builder_names = ("build-template-agent", "build-custom-agent")
        for name in builder_names:
            skill = ROOT / "skills" / name
            with self.subTest(skill=name):
                self.assertFalse((skill / "references/mcp.md").exists())
                instructions = (skill / "SKILL.md").read_text(encoding="utf-8")
                self.assertNotIn("list_observations", instructions)
                self.assertIn(
                    "not independently verified",
                    " ".join(instructions.split()),
                )

        sync_source = (ROOT / "scripts/sync_skill_refs.py").read_text(encoding="utf-8")
        module = ast.parse(sync_source)
        mcp_skills_assignment = next(
            node
            for node in module.body
            if isinstance(node, ast.Assign)
            and len(node.targets) == 1
            and isinstance(node.targets[0], ast.Name)
            and node.targets[0].id == "MCP_SKILLS"
        )
        synced_skills = ast.literal_eval(mcp_skills_assignment.value)
        for name in builder_names:
            self.assertNotIn(name, synced_skills)

    def test_both_builders_use_the_process_reported_dynamic_ui_port(self) -> None:
        starters = {
            "build-template-agent": ROOT
            / "skills/build-template-agent/assets/release-evidence-reviewer",
            "build-custom-agent": ROOT
            / "skills/build-custom-agent/assets/custom-agent-starter",
        }

        for name, starter in starters.items():
            with self.subTest(builder=name):
                program = (starter / "Program.cs").read_text(encoding="utf-8")
                instructions = (
                    ROOT / "skills" / name / "SKILL.md"
                ).read_text(encoding="utf-8")
                command = (ROOT / "commands" / f"{name}.md").read_text(
                    encoding="utf-8"
                )

                self.assertNotIn("Microsoft.Hosting.Lifetime", program)
                self.assertNotIn("Local UI:", program)
                self.assertNotIn("PublicListenUrl", program)
                self.assertIn("--urls http://127.0.0.1:0", instructions)
                self.assertIn("Now listening on:", instructions)
                self.assertIn(f"Use the `{name}` skill", command)

    def test_every_template_keeps_secret_and_ui_runtime_contract(self) -> None:
        for item in TEMPLATES:
            with self.subTest(template=item["id"]):
                source = TEMPLATE_SKILL / "assets" / item["id"]
                program = (source / "Program.cs").read_text(encoding="utf-8")
                self.assertNotIn("Microsoft.Hosting.Lifetime", program)
                self.assertIn("RecordInputs = false", program)
                self.assertIn("RecordOutputs = false", program)
                self.assertIn('"/api/health"', program)
                self.assertFalse(any(source.glob(".env*")))


if __name__ == "__main__":
    unittest.main()
