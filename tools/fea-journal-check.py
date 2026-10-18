"""Exercise real Windows file-sharing races and initial-heartbeat cleanup."""
from __future__ import annotations
import ctypes
from ctypes import wintypes
import importlib.util
import json
from pathlib import Path
import tempfile
import threading
import time

import psutil

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("febio_journal_check",ROOT/"tools/fea-febio-spike.py")
febio=importlib.util.module_from_spec(spec);spec.loader.exec_module(febio)
io=febio.ccx
kernel=ctypes.WinDLL("kernel32",use_last_error=True)
kernel.CreateFileW.argtypes=[wintypes.LPCWSTR,wintypes.DWORD,wintypes.DWORD,wintypes.LPVOID,wintypes.DWORD,wintypes.DWORD,wintypes.HANDLE]
kernel.CreateFileW.restype=wintypes.HANDLE
kernel.CloseHandle.argtypes=[wintypes.HANDLE];kernel.CloseHandle.restype=wintypes.BOOL


def handle(path,sharing):
    result=kernel.CreateFileW(str(path),0x80000000,sharing,None,3,0,None)
    if result==ctypes.c_void_p(-1).value: raise ctypes.WinError(ctypes.get_last_error())
    return result


def check():
    with tempfile.TemporaryDirectory(prefix="fea-journal-") as temporary:
        folder=Path(temporary);path=folder/"status.json";io.write_json_atomic(path,{"revision":0})
        locked=handle(path,3);timer=threading.Timer(.2,lambda:kernel.CloseHandle(locked));timer.start();start=time.perf_counter()
        try: io.write_json_atomic(path,{"revision":1})
        finally: timer.join()
        elapsed=time.perf_counter()-start
        if elapsed<.15 or json.loads(path.read_text())!={"revision":1}: raise AssertionError("Transient sharing lock was not retried atomically")
        # This Windows host may still reject MoveFileEx while a delete-sharing
        # handle is open. Reader handles must stay brief; retries cover both modes.
        shared=handle(path,7);timer=threading.Timer(.2,lambda:kernel.CloseHandle(shared));timer.start()
        try: io.write_json_atomic(path,{"revision":2})
        finally: timer.join()
        if json.loads(path.read_text())!={"revision":2}: raise AssertionError("Delete-sharing reader race was not recovered")

        # Persistent first-heartbeat failure must clean up its just-launched child.
        launched=[];original_write=io.write_json_atomic;original_launch=febio.subprocess.Popen
        def launch(*args,**kwargs):
            process=original_launch(*args,**kwargs);launched.append(process.pid);return process
        def fail(*args,**kwargs): raise OSError("Deliberate persistent journal failure")
        io.write_json_atomic=fail;febio.subprocess.Popen=launch
        try:
            try: febio.invoke(folder/"failed-heartbeat",(ROOT/"artifacts/febio-verification/coupon-0_0060-0.30-896b952b8506/case.feb").read_text(),timeout=5)
            except OSError: pass
            else: raise AssertionError("Persistent journal failure was accepted")
        finally: io.write_json_atomic=original_write;febio.subprocess.Popen=original_launch
        if not launched or any(psutil.pid_exists(pid) for pid in launched): raise AssertionError("Initial heartbeat failure left a solver alive")
    print(json.dumps({"status":"passed","actualSharingLockRetrySeconds":elapsed,"deleteSharingReaderRaceRecovered":True,"initialHeartbeatFailureChildCleanedUp":True}))


if __name__=="__main__": check()
