"""Real Windows cross-process locking and duplicate-worker rejection checks.

Never starts FEBio; the child process only tries the advisory file lock.
"""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/"tools/fea-febio-spike.py"
spec=importlib.util.spec_from_file_location("worker",SOURCE)
worker=importlib.util.module_from_spec(spec);spec.loader.exec_module(worker)


def run():
    original_out=worker.OUT
    original_processes=worker.psutil.process_iter
    original_launch=worker.subprocess.Popen
    with tempfile.TemporaryDirectory(prefix="fea-worker-lock-") as temporary:
        worker.OUT=Path(temporary)
        program=("import importlib.util,pathlib,sys; s=importlib.util.spec_from_file_location('f',sys.argv[1]); "
                 "f=importlib.util.module_from_spec(s); s.loader.exec_module(f); f.OUT=pathlib.Path(sys.argv[2]);\n"
                 "with f.worker_lock(): pass")
        def child():
            return subprocess.run([sys.executable,"-c",program,str(SOURCE),temporary],capture_output=True,text=True,
                                  timeout=15,creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            with worker.worker_lock():
                result=child()
                assert result.returncode and "Another FEBio wrapper owns the worker lock" in result.stderr
            # A separate process must acquire the same file after release.
            result=child()
            assert result.returncode==0,result.stderr
            try:
                with worker.worker_lock(): raise ValueError("deliberate wrapper failure")
            except ValueError: pass
            result=child()
            assert result.returncode==0,result.stderr
            # A legacy/orphan native worker does not own the new file lock.
            # Its process identity must still prevent any launch or deck write.
            worker.psutil.process_iter=lambda *args:[SimpleNamespace(pid=123456,info={"name":"FEBio4.EXE"})]
            def no_launch(*args,**kwargs): raise AssertionError("Launched a duplicate solver")
            worker.subprocess.Popen=no_launch
            target=Path(temporary)/"rejected"
            try: worker.invoke(target,"no solver may read this")
            except RuntimeError as error: assert "Another native FEBio worker" in str(error)
            else: raise AssertionError("Accepted duplicate native worker")
            assert not target.exists()
        finally:
            worker.OUT=original_out
            worker.psutil.process_iter=original_processes
            worker.subprocess.Popen=original_launch
    print(json.dumps({"status":"passed","crossProcessContentionRejected":True,"normalRelease":True,
                      "exceptionRelease":True,"legacyNativeWorkerRejectedBeforeLaunch":True}))


if __name__=="__main__": run()
