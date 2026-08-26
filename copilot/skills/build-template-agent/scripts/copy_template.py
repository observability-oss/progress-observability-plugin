#!/usr/bin/env python3
"""Copy the fixed Release Evidence Reviewer asset into a new target folder."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil
import stat
import sys
import tempfile


SKILL_ROOT = Path(__file__).resolve().parents[1]
TEMPLATE_ROOT = SKILL_ROOT / "assets" / "release-evidence-reviewer"
DEFAULT_TARGET = Path("release-evidence-reviewer")
EXCLUDED_NAMES = frozenset({"bin", "obj", "__pycache__", ".git", ".vs", ".DS_Store"})


class CopyRefusedError(RuntimeError):
    """Raised when copying would be ambiguous or unsafe."""


def _is_link(path: Path) -> bool:
    try:
        return stat.S_ISLNK(path.lstat().st_mode)
    except FileNotFoundError:
        return False


def _is_empty_directory(path: Path) -> bool:
    if _is_link(path) or not path.is_dir():
        return False
    try:
        next(path.iterdir())
    except StopIteration:
        return True
    return False


def _validate_source(source: Path) -> None:
    if not source.is_dir():
        raise CopyRefusedError(f"Template asset is missing: {source}")
    if _is_link(source):
        raise CopyRefusedError(f"Template asset must not be a symlink: {source}")

    for entry in source.rglob("*"):
        if _is_link(entry):
            raise CopyRefusedError(f"Template asset contains a symlink: {entry}")


def _copy_ignore(_directory: str, names: list[str]) -> list[str]:
    return sorted(
        name
        for name in names
        if name in EXCLUDED_NAMES
        or name == ".env"
        or (name.startswith(".env.") and name != ".env.example")
    )


def _resolve_target(target: Path, source: Path) -> tuple[Path, bool]:
    if ".." in target.parts:
        raise CopyRefusedError(f"Target must not contain '..': {target}")

    if os.path.lexists(target) and _is_link(target):
        raise CopyRefusedError(f"Target must not be a symlink: {target}")

    parent = target.parent
    if not parent.exists():
        raise CopyRefusedError(f"Target parent does not exist: {parent}")
    if not parent.is_dir():
        raise CopyRefusedError(f"Target parent is not a directory: {parent}")

    resolved_parent = parent.resolve(strict=True)
    resolved_target = resolved_parent / target.name
    target_existed = os.path.lexists(resolved_target)
    if target_existed and not _is_empty_directory(resolved_target):
        raise CopyRefusedError(
            f"Target exists and is not an empty real directory: {target}"
        )

    resolved_source = source.resolve()
    if (
        resolved_target == resolved_source
        or resolved_source in resolved_target.parents
    ):
        raise CopyRefusedError(f"Target must be outside the template asset: {target}")

    return resolved_target, target_existed


def copy_template(target: Path, *, source: Path = TEMPLATE_ROOT) -> Path:
    """Copy *source* into a missing or empty *target* and return its absolute path."""

    source = Path(source)
    target = Path(target)
    _validate_source(source)
    resolved_target, target_existed = _resolve_target(target, source)

    staging = Path(
        tempfile.mkdtemp(
            prefix=f".{resolved_target.name}.copy-",
            dir=str(resolved_target.parent),
        )
    )
    try:
        shutil.copytree(
            source,
            staging,
            dirs_exist_ok=True,
            copy_function=shutil.copy2,
            ignore=_copy_ignore,
        )
        shutil.copystat(source, staging, follow_symlinks=False)
        if target_existed:
            if not _is_empty_directory(resolved_target):
                raise CopyRefusedError(
                    f"Target changed during copy; refusing to replace it: {target}"
                )
        elif os.path.lexists(resolved_target):
            raise CopyRefusedError(
                f"Target appeared during copy; refusing to overwrite it: {target}"
            )
        os.replace(staging, resolved_target)
    except BaseException:
        shutil.rmtree(staging, ignore_errors=True)
        raise

    return resolved_target


def _parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Copy the fixed Release Evidence Reviewer template."
    )
    parser.add_argument(
        "--target",
        type=Path,
        default=DEFAULT_TARGET,
        help="missing or empty project directory (default: ./release-evidence-reviewer)",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = _parse_args(argv)
    try:
        copied = copy_template(args.target)
    except (CopyRefusedError, OSError) as error:
        print(f"copy-template: {error}", file=sys.stderr)
        return 2

    print(copied)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
