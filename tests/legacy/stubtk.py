import sys, types
def _mk(name):
    m = types.ModuleType(name)
    m.__getattr__ = lambda n: type(n, (), {"__init__": lambda self, *a, **k: None})
    sys.modules[name] = m
    return m
tk = _mk("tkinter")
for n in ("filedialog", "messagebox", "ttk"):
    m = _mk("tkinter." + n); setattr(tk, n, m)
