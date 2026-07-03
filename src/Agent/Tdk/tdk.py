r"""tdk - helper module for Edda TDK validator scripts.

Shipped by the sandbox next to the validator script (Docker: /workspace/tdk.py;
wasm/subprocess: alongside the script in its temp working directory). Import it to
skip the JSON stdin/stdout boilerplate:

    from tdk import validator, violation

    @validator(languages=["python"])
    def check(code, ctx):
        for m in ctx.finditer(r"except:\s*pass"):
            yield violation("Bare except: pass swallows errors",
                            line=ctx.line_of(m), severity="warning",
                            suggestion="Catch a specific exception and handle it.")

Raw stdin/stdout scripts stay valid: a script that never imports tdk is unaffected -
the module only activates when imported and a validator is registered.
"""
from __future__ import annotations
import ast as _ast
import atexit as _atexit
import json as _json
import os as _os
import re as _re
import sys as _sys

_VALIDATORS = []   # list of (func, [languages])
_RAN = False       # runner fires at most once


def validator(func=None, *, languages=None):
    """Register a validator. Usage: @validator  or  @validator(languages=["python"])."""
    langs = [str(l).lower() for l in (languages or [])]

    def _register(f):
        _VALIDATORS.append((f, langs))
        return f

    return _register(func) if callable(func) else _register


def violation(message, *, line=None, severity="error", suggestion=None):
    """Build a violation dict. rule_id is filled in by the runner from the input."""
    v = {"message": str(message), "severity": str(severity)}
    if line is not None:
        v["line"] = int(line)
    if suggestion is not None:
        v["suggestion"] = str(suggestion)
    return v


class _Ctx:
    """Validation context passed to each validator as the second argument."""

    def __init__(self, data):
        self.code = data.get("code", "") or ""
        self.language = (data.get("language") or "").lower()
        self.rule_id = data.get("rule_id", "") or ""
        self.user_message = data.get("user_message", "") or ""

    def finditer(self, pattern, flags=0):
        """Iterate re.Match objects for pattern over the code block."""
        return _re.finditer(pattern, self.code, flags)

    def line_of(self, match_or_pos):
        """1-based line number of a re.Match or an integer offset within the code."""
        pos = match_or_pos.start() if hasattr(match_or_pos, "start") else int(match_or_pos)
        return self.code.count("\n", 0, pos) + 1

    def python_ast(self):
        """Parse the code block with the stdlib ast module (Python blocks only)."""
        return _ast.parse(self.code)


def _run():
    global _RAN
    if _RAN or not _VALIDATORS:
        return
    _RAN = True
    data = _json.load(_sys.stdin)
    ctx = _Ctx(data)
    violations = []
    for func, langs in _VALIDATORS:
        if langs and ctx.language and ctx.language not in langs:
            continue
        for v in func(ctx.code, ctx) or []:
            if "rule_id" not in v:
                v["rule_id"] = ctx.rule_id
            violations.append(v)
    _json.dump({"pass": len(violations) == 0, "violations": violations}, _sys.stdout)
    _sys.stdout.flush()


def _run_safely():
    if not _VALIDATORS:
        return
    try:
        _run()
    except Exception:  # surface as an engine error (ExitCode 1), not a rule failure
        import traceback
        traceback.print_exc(file=_sys.stderr)
        _sys.stderr.flush()
        _os._exit(1)


_atexit.register(_run_safely)
