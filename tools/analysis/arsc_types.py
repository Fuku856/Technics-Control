import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import arsc
arsc.main_types(sys.argv[1], sys.argv[2], sys.argv[3])
