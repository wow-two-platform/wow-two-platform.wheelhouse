import http.server
import io
import json
import threading
import unittest
import self_check


class Console(http.server.BaseHTTPRequestHandler):
    """A console stand-in: answers by path, and records the host each request named."""
    answers = {}
    hosts = []

    def log_message(self, *arguments):
        pass

    def answer(self):
        self.hosts.append(self.headers.get("Host"))
        status, body = self.answers.get((self.command, self.path), (404, b""))
        self.send_response(status)
        if status in (301, 302, 307):
            self.send_header("Location", "https://elsewhere.example/")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        self.answer()

    def do_POST(self):
        self.rfile.read(int(self.headers.get("Content-Length", 0)))
        self.answer()


HEALTHY = {("GET", "/api/system/ready"): (200, b""),
           ("GET", "/api/system/status"): (200, json.dumps({"service": "Wheelhouse", "status": "ok"}).encode()),
           ("POST", "/mcp"): (401, b"")}


class SelfCheckTests(unittest.TestCase):
    def setUp(self):
        Console.answers, Console.hosts = dict(HEALTHY), []
        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Console)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.base = "http://127.0.0.1:" + str(self.server.server_address[1])

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()

    def run_check(self, environment=None):
        output = io.StringIO()
        code = self_check.main({"VERIFY_SERVICE_CONSOLE": self.base} if environment is None else environment,
                               output=output)
        return code, output.getvalue().strip().splitlines()

    def test_a_healthy_console_passes_every_check_by_its_localhost_name(self):
        code, lines = self.run_check()
        self.assertEqual(0, code)
        self.assertEqual("Passed: readiness, status and anonymous MCP refusal", lines[-1])
        self.assertEqual({"localhost"}, set(Console.hosts), "every console allows localhost for its own probes")

    def test_each_failure_is_named_on_the_last_line(self):
        for answers, failed in (
                ({("GET", "/api/system/ready"): (503, b"")}, "readiness"),
                ({("GET", "/api/system/status"): (200, b'{"service": "Other"}')}, "status"),
                ({("GET", "/api/system/status"): (200, b"not json")}, "status"),
                ({("POST", "/mcp"): (200, b'{"result": {}}')}, "anonymous MCP refused"),
                # A redirect is not a refusal: an HTTPS redirect would hide an open MCP endpoint.
                ({("POST", "/mcp"): (307, b"")}, "anonymous MCP refused")):
            with self.subTest(failed=failed, answers=answers):
                Console.answers = {**HEALTHY, **answers}
                code, lines = self.run_check()
                self.assertEqual((1, "Failed: " + failed), (code, lines[-1]))

    def test_an_unreachable_console_fails_every_check_without_raising(self):
        self.tearDown()
        code, lines = self.run_check()
        self.setUp()
        self.assertEqual((1, "Failed: readiness, status, anonymous MCP refused"), (code, lines[-1]))
        self.assertIn("failed readiness (no answer)", lines)

    def test_a_missing_address_is_a_usage_error(self):
        code, lines = self.run_check({})
        self.assertEqual((2, "No console address: VERIFY_SERVICE_CONSOLE is not set"), (code, lines[-1]))


if __name__ == "__main__":
    unittest.main()
