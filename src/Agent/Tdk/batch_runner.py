"""batch_runner - executes many TDK validator jobs in one sandbox (F11).

Reads {"jobs":[{"id":<int>,"script":<str>,"input":<obj>}]} from stdin. For each job it writes the
validator script next to this runner (so `import tdk` resolves) and runs it as a subprocess with
the job's input JSON on stdin - identical semantics to running the validator alone. Prints
{"results":[{"id","exit_code","stdout","stderr","timed_out"}]} to stdout.
"""
import json as _json
import os as _os
import subprocess as _subprocess
import sys as _sys

_PER_JOB_TIMEOUT = 10


def _main():
    data = _json.load(_sys.stdin)
    workdir = _os.getcwd()
    results = []
    for job in data.get("jobs", []):
        jid = job["id"]
        path = _os.path.join(workdir, "job_%d.py" % jid)
        with open(path, "w") as handle:
            handle.write(job["script"])
        try:
            proc = _subprocess.run(
                [_sys.executable, path],
                input=_json.dumps(job["input"]),
                capture_output=True, text=True, timeout=_PER_JOB_TIMEOUT)
            results.append({"id": jid, "exit_code": proc.returncode,
                            "stdout": proc.stdout, "stderr": proc.stderr, "timed_out": False})
        except _subprocess.TimeoutExpired as exc:
            results.append({"id": jid, "exit_code": 1,
                            "stdout": exc.stdout or "", "stderr": "validator timed out", "timed_out": True})
        except Exception as exc:  # noqa: BLE001 - surface any runner error per job
            results.append({"id": jid, "exit_code": 1, "stdout": "", "stderr": str(exc), "timed_out": False})
        finally:
            try:
                _os.remove(path)
            except OSError:
                pass
    _json.dump({"results": results}, _sys.stdout)


_main()
