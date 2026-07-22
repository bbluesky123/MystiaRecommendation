try:
    import UnityPy
    print("UnityPy OK version=" + str(getattr(UnityPy, "__version__", "unknown")))
except ImportError as e:
    print("UnityPy NOT available: " + str(e))