#!/usr/bin/env bash
# ==============================================================================
# Headless / Background Blender 5.2 MCP Socket Daemon (`localhost:9876`)
# ==============================================================================
# Starts Blender 5.2 LTS in background mode with the `blender_mcp` TCP socket
# server listening on localhost:${BLENDER_PORT:-9876}, Poly Haven CC0 enabled,
# and a main-thread command-queue drainer loop so AI agents can use `blender-mcp`
# tools (`get_scene_info`, `execute_blender_code`, `get_viewport_screenshot`,
# `export_scene`, Poly Haven) even when no interactive Blender GUI window is open.
# ==============================================================================
set -euo pipefail

PORT="${BLENDER_PORT:-9876}"

if ss -tln | grep -q ":${PORT} "; then
  echo "Blender MCP socket server is already listening on localhost:${PORT}."
  exit 0
fi

exec /usr/bin/blender --background --python-expr "
import bpy, time, socket, threading, signal, sys, addon_utils
addon_utils.enable('blender_mcp', default_set=True)
import blender_mcp

port = int('${PORT}')
server = blender_mcp.BlenderMCPServer(host='localhost', port=port)
bpy.types.blendermcp_server = server
bpy.context.scene.blendermcp_use_polyhaven = True

server.running = True
server.socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
server.socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
server.socket.bind((server.host, server.port))
server.socket.listen(5)
server.server_thread = threading.Thread(target=server._server_loop, daemon=True)
server.server_thread.start()

print(f'BlenderMCP Headless Daemon listening on localhost:{port} (Protocol {blender_mcp.ADDON_PROTOCOL_VERSION}, Poly Haven CC0 enabled)', flush=True)

def _shutdown(signum, frame):
    server.stop()
    sys.exit(0)

signal.signal(signal.SIGINT, _shutdown)
signal.signal(signal.SIGTERM, _shutdown)

while server.running:
    server._drain_command_queue()
    time.sleep(0.02)
"
