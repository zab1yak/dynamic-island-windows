"""Offline regression fixtures for the SVG-to-WPF geometry importer.

Run: python -m unittest discover -s tools -p 'test_import_lucide.py'
"""
import io
import json
import tarfile
import unittest
from unittest.mock import patch

from import_lucide import VERSION, convert, independent_path, read_package


class IndependentSvgPathTests(unittest.TestCase):
    def test_close_cross_resets_second_element_and_keeps_relative_line(self):
        svg = '<svg><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>'
        self.assertEqual("M18 6 6 18 M6,6 l12 12", convert(svg))

    def test_initial_relative_move_does_not_turn_implicit_offsets_absolute(self):
        self.assertEqual("M15,18 l-6-6 6-6", independent_path("m15 18-6-6 6-6"))
        self.assertEqual("M9,18 l6-6-6-6", independent_path("m9 18 6-6-6-6"))

    def test_explicit_relative_commands_remain_relative(self):
        self.assertEqual("M11,7 l-3 5h4l-3 5", independent_path("m11 7-3 5h4l-3 5"))
        self.assertEqual("M2,3h4v5", independent_path("m2,3h4v5"))

    def test_later_relative_subpath_keeps_its_original_origin(self):
        self.assertEqual("M2,3 l4,5zm1,2 3,4", independent_path("m2,3 4,5zm1,2 3,4"))

    def test_absolute_path_and_implicit_absolute_lines_are_preserved(self):
        data = "M20 6 9 17l-5-5"
        self.assertEqual(data, independent_path(data))

    def test_signed_decimal_and_exponent_coordinates(self):
        self.assertEqual("M+1e1,-2E-1 l.5-.6", independent_path("m+1e1,-2E-1.5-.6"))
        self.assertEqual("M.5,.6 l1,2", independent_path("m.5.6,1,2"))

    def test_relative_path_after_closed_shapes_resets_to_svg_origin(self):
        svg = '<svg><circle cx="12" cy="12" r="10"/><path d="m16 9-5.5 5.5L8 12"/></svg>'
        self.assertTrue(convert(svg).endswith(" Z M16,9 l-5.5 5.5L8 12"))

    def test_every_element_path_resets_independently_across_shape_types(self):
        svg = ('<svg><line x1="1" y1="2" x2="20" y2="21"/>'
               '<path d="m2 3 4 5"/><rect x="2" y="3" width="5" height="6"/>'
               '<path d="m7 8 1 2"/></svg>')
        self.assertEqual("M1,2 L20,21 M2,3 l4 5 M2,3 H7 V9 H2 Z M7,8 l1 2", convert(svg))

    def test_incomplete_moveto_is_rejected(self):
        for data in ("m1", "m1 2 3", "mh4"):
            with self.subTest(data=data), self.assertRaises(ValueError):
                independent_path(data)


class PackageValidationTests(unittest.TestCase):
    @staticmethod
    def package(version=VERSION, symbolic=False):
        data = io.BytesIO()
        with tarfile.open(fileobj=data, mode="w:gz") as archive:
            for name, content in (("package/package.json", json.dumps({"name": "lucide-static", "version": version})),
                                  ("package/icons/x.svg", '<svg><path d="m6 6 12 12"/></svg>')):
                member = tarfile.TarInfo(name)
                payload = content.encode()
                member.size = len(payload)
                if symbolic and name.endswith("x.svg"):
                    member.type = tarfile.SYMTYPE
                    member.linkname = "../../outside.svg"
                    member.size = 0
                archive.addfile(member, io.BytesIO(payload))
        return data.getvalue()

    def test_pinned_package_reads_only_requested_icons(self):
        with patch("import_lucide.ICONS", {"Close": "x"}):
            self.assertEqual({"x": '<svg><path d="m6 6 12 12"/></svg>'}, read_package(self.package()))

    def test_mismatched_package_version_is_rejected(self):
        with patch("import_lucide.ICONS", {"Close": "x"}), self.assertRaises(ValueError):
            read_package(self.package(version="0.0.0"))

    def test_symbolic_archive_members_are_rejected(self):
        with patch("import_lucide.ICONS", {"Close": "x"}), self.assertRaises(ValueError):
            read_package(self.package(symbolic=True))


if __name__ == "__main__":
    unittest.main()
