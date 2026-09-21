"""Every from-import between the tools resolves to a name the target actually binds.

The gate iteration 41 of the cleanup effort exists to justify: cleanup 7 deleted `pairs.number`
while `load.py` imported it, and `load.py` would not import for six days across four green test
passes, because nothing imports the tools — several run a report at module level (`verdict.py`
says so in its own docstring), so a test cannot simply import them all. This parses instead:
every `.py` under `tools/corpus` and `tools/lanes` must compile, and every `from X import Y`
naming a *local* module must name something `X` binds at module level.

**The stated limit**: this resolves from-imports, not uses. A deleted `scoring.gone` still
breaks `scoring.gone()` at run time; what it can never miss again is the class that actually
bit — a consolidation deleting a name a sibling still imports.
"""
import ast
import os
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
ROOTS = [HERE, os.path.normpath(os.path.join(HERE, "..", "lanes"))]


# tool files operation.
def tool_files():
    files = []
    for root in ROOTS:
        for name in sorted(os.listdir(root)):
            if name.endswith(".py") and not name.startswith("test_"):
                files.append(os.path.join(root, name))
    return files


# module bindings operation.
def module_bindings(tree):
    """Every name a module binds at its top level."""
    names = set()
    for node in tree.body:
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            names.add(node.name)
        elif isinstance(node, ast.Assign):
            for target in node.targets:
                if isinstance(target, ast.Name):
                    names.add(target.id)
                elif isinstance(target, (ast.Tuple, ast.List)):
                    for element in target.elts:
                        if isinstance(element, ast.Name):
                            names.add(element.id)
        elif isinstance(node, ast.AnnAssign) and isinstance(node.target, ast.Name):
            names.add(node.target.id)
        elif isinstance(node, ast.Import):
            for alias in node.names:
                names.add((alias.asname or alias.name).split(".")[0])
        elif isinstance(node, ast.ImportFrom):
            for alias in node.names:
                if alias.name != "*":
                    names.add(alias.asname or alias.name)
        elif isinstance(node, (ast.If, ast.Try, ast.For, ast.While)):
            for inner in ast.walk(node):
                if isinstance(inner, (ast.FunctionDef, ast.ClassDef)):
                    names.add(inner.name)
                elif isinstance(inner, ast.Assign):
                    for target in inner.targets:
                        if isinstance(target, ast.Name):
                            names.add(target.id)
    return names


class TheLanesSuiteIsReachedByTheOneDiscoveryCommand(unittest.TestCase):
    """`tools/lanes`' tests were built, documented and run by nothing (`D2`'s class, in the test
    tooling itself): the documented discovery starts at `tools/corpus`, no command anywhere named
    `test_lanes.py`, and `discover -s tools` finds zero tests and exits OK — a discovery that
    judged nothing, passing (`E8`'s shape, in the runner). This bridges them: the one command
    everyone runs loads and runs the lanes suite too, and fails if it shrinks to nothing.
    """

# test the lanes tests are found and pass operation.
    def test_the_lanes_tests_are_found_and_pass(self):
        lanes = os.path.normpath(os.path.join(HERE, "..", "lanes"))
        suite = unittest.TestLoader().discover(
            start_dir=lanes, pattern="test_*.py", top_level_dir=lanes)

        count = suite.countTestCases()
        self.assertGreaterEqual(count, 5,
                                "the lanes suite came back with %d cases, so the bridge is not"
                                " reaching it (E8)" % count)

        result = unittest.TestResult()
        suite.run(result)
        problems = ["%s: %s" % (case, trace.strip().splitlines()[-1])
                    for case, trace in result.failures + result.errors]
        self.assertEqual([], problems)


class EveryToolStillImportsItsSiblings(unittest.TestCase):
    __doc__ = __doc__

# test every local from import names something the target binds operation.
    def test_every_local_from_import_names_something_the_target_binds(self):
        trees = {}
        for path in tool_files():
            with open(path, encoding="utf-8") as handle:
                trees[os.path.splitext(os.path.basename(path))[0]] = (
                    path, ast.parse(handle.read(), filename=path))

        self.assertGreaterEqual(len(trees), 15,
                                "the scan found almost no tools, so it is not looking at them (E8)")

        broken = []
        checked = 0
        for name, (path, tree) in trees.items():
            for node in ast.walk(tree):
                if not isinstance(node, ast.ImportFrom) or node.module not in trees:
                    continue
                bound = module_bindings(trees[node.module][1])
                for alias in node.names:
                    if alias.name == "*":
                        continue
                    checked += 1
                    if alias.name not in bound:
                        broken.append("%s imports %s from %s, which does not bind it"
                                      % (name, alias.name, node.module))

        self.assertGreater(checked, 5,
                           "the scan resolved almost no from-imports, so it proves nothing (E8)")
        self.assertEqual([], broken)
