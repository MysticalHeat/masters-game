SHELL := /usr/bin/env bash

.DEFAULT_GOAL := help

PYTHON ?= python3
PIP ?= pip3
LOGS_DIR ?= $(CURDIR)/Logs
CPU_LLAMA_SERVER ?= $(CURDIR)/tools/llama.cpp/llama-b8913/llama-server
VULKAN_LLAMA_SERVER ?= $(CURDIR)/tools/llama.cpp/llama-b8913-vulkan/llama-server
LLAMA_SERVER ?= $(VULKAN_LLAMA_SERVER)
RECOMMENDED_MODEL ?= $(CURDIR)/Assets/Models/Qwen/qwen2.5-3b-instruct-q4_k_m.gguf
LARGE_MODEL ?= $(CURDIR)/Assets/Models/Qwen/qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf
CPU_MODEL ?= $(CURDIR)/Assets/Models/Qwen/qwen2.5-3b-instruct-q4_k_m.gguf
MODEL ?= $(RECOMMENDED_MODEL)
HOST ?= 127.0.0.1
PORT ?= 8080
CTX_SIZE ?= 4096
GPU_LAYERS ?= 999
MODEL_ALIAS ?= qwen
TEST_PROMPT ?= Привет. Кратко представься по-русски.
EXTRA_ARGS ?=
UPSTREAM_HOST ?= $(HOST)
UPSTREAM_PORT ?= 8081
PROXY_HOST ?= $(HOST)
PROXY_PORT ?= $(PORT)
PROXY_UPSTREAM ?= http://$(UPSTREAM_HOST):$(UPSTREAM_PORT)
PROXY_SCRIPT ?= $(CURDIR)/tools/llama_logging_proxy.py
PROXY_LOG ?= $(LOGS_DIR)/llama-proxy.log
SERVER_LOG ?= $(LOGS_DIR)/llama-server.log
VOICE_BACKEND_DIR ?= $(CURDIR)/tools/voice-backend
VOICE_BACKEND_REQUIREMENTS ?= $(VOICE_BACKEND_DIR)/requirements.txt
VOICE_BACKEND_VENV ?= $(VOICE_BACKEND_DIR)/.venv
VOICE_BACKEND_PYTHON ?= $(VOICE_BACKEND_VENV)/bin/python
VOICE_STT_PORT ?= 8081
VOICE_TTS_PORT ?= 8082
VOICE_STT_BACKEND ?= whisper
VOICE_TTS_BACKEND ?= piper
VOICE_STT_MODEL ?= small
VOICE_TTS_PIPER_MODEL ?= $(CURDIR)/tools/voice-backend/models/ru_RU-denis-medium/ru_RU-denis-medium.onnx
VOICE_TTS_PIPER_CONFIG ?= $(CURDIR)/tools/voice-backend/models/ru_RU-denis-medium/ru_RU-denis-medium.onnx.json
VOICE_TTS_PIPER_USE_SPEAKER ?= false
VOICE_TTS_PIPER_BIN ?= $(CURDIR)/tools/voice-backend/piper-release/piper/piper

.PHONY: help llama-server llama-server-vulkan llama-server-cpu llama-server-large llama-server-upstream llama-proxy llama-stack llama-health llama-health-upstream llama-chat-test voice-backend voice-backend-install voice-backend-health

help:
	@printf '%s\n' \
		'Targets:' \
		'  make llama-server [MODEL=/abs/path/to/model.gguf PORT=8080 CTX_SIZE=4096 GPU_LAYERS=999 MODEL_ALIAS=qwen]' \
		'  make llama-server-upstream [MODEL=/abs/path/to/model.gguf UPSTREAM_PORT=8081 CTX_SIZE=4096 GPU_LAYERS=999 MODEL_ALIAS=qwen]' \
		'  make llama-proxy [PROXY_PORT=8080 PROXY_UPSTREAM=http://127.0.0.1:8081 PROXY_LOG=Logs/llama-proxy.log]' \
		'  make llama-stack [MODEL=/abs/path/to/model.gguf PROXY_PORT=8080 UPSTREAM_PORT=8081 MODEL_ALIAS=qwen]' \
		'  make llama-server-vulkan [MODEL=/abs/path/to/model.gguf PORT=8080 CTX_SIZE=4096 GPU_LAYERS=999 MODEL_ALIAS=qwen]' \
		'  make llama-server-cpu [MODEL=/abs/path/to/model.gguf PORT=8080 CTX_SIZE=4096 MODEL_ALIAS=qwen]' \
		'  make llama-server-large [PORT=8080 CTX_SIZE=4096 GPU_LAYERS=999 MODEL_ALIAS=qwen]' \
		'  make llama-health [HOST=127.0.0.1 PORT=8080]' \
		'  make llama-health-upstream [UPSTREAM_HOST=127.0.0.1 UPSTREAM_PORT=8081]' \
		'  make llama-chat-test [HOST=127.0.0.1 PORT=8080 MODEL_ALIAS=qwen TEST_PROMPT="..."]' \
		'  make voice-backend [VOICE_STT_PORT=8081 VOICE_TTS_PORT=8082 VOICE_STT_BACKEND=whisper VOICE_TTS_BACKEND=piper]' \
		'  make voice-backend-install [PYTHON=python3 PIP=pip3]' \
		'  make voice-backend-health [PORT=8081]' \
		'' \
		'Defaults:' \
		'  LLAMA_SERVER       -> tools/llama.cpp/llama-b8913-vulkan/llama-server' \
		'  llama-server      -> Vulkan + Assets/Models/Qwen/qwen2.5-3b-instruct-q4_k_m.gguf' \
		'  llama-server-upstream -> same server on 127.0.0.1:8081 with verbose server logs' \
		'  llama-proxy       -> logging proxy on 127.0.0.1:8080 forwarding to 127.0.0.1:8081' \
		'  llama-stack       -> starts upstream llama-server and proxy together' \
		'  llama-server-large -> Vulkan + Assets/Models/Qwen/qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf' \
		'  llama-server-cpu -> Assets/Models/Qwen/qwen2.5-3b-instruct-q4_k_m.gguf' \
		'  voice-backend -> FastAPI voice backend on 8081 and 8082' \
		'' \
		'Laptop note (Core Ultra 9 + Intel Arc iGPU):' \
		'  recommended default is make llama-server (Vulkan + 3B)' \
		'  for full request/response logging use make llama-stack' \
		'' \
		'Examples:' \
		'  make llama-server' \
		'  make llama-stack' \
		'  make llama-proxy' \
		'  make llama-server-large' \
		'  make llama-server-cpu' \
		'  make llama-health' \
		'  make llama-chat-test' \
		'  make llama-server MODEL=/models/custom.gguf' \
		'  make voice-backend'

llama-server:
	@$(MAKE) llama-server-vulkan \
		LLAMA_SERVER="$(LLAMA_SERVER)" \
		MODEL="$(MODEL)" \
		HOST="$(HOST)" \
		PORT="$(PORT)" \
		CTX_SIZE="$(CTX_SIZE)" \
		GPU_LAYERS="$(GPU_LAYERS)" \
		MODEL_ALIAS="$(MODEL_ALIAS)" \
		EXTRA_ARGS="$(EXTRA_ARGS)"

llama-server-vulkan:
	@if [ ! -x "$(LLAMA_SERVER)" ] && ! command -v "$(LLAMA_SERVER)" >/dev/null 2>&1; then \
		printf 'llama-server not found in PATH.\n' >&2; \
		printf 'Set LLAMA_SERVER=/path/to/llama-server or install/download llama.cpp.\n' >&2; \
		exit 1; \
	fi
	@if [ ! -f "$(MODEL)" ]; then \
		printf 'Model file not found: %s\n' "$(MODEL)" >&2; \
		printf 'Pass MODEL=/absolute/path/to/model.gguf\n' >&2; \
		exit 1; \
	fi
	"$(LLAMA_SERVER)" \
		-m "$(MODEL)" \
		--host "$(HOST)" \
		--port "$(PORT)" \
		-c "$(CTX_SIZE)" \
		-ngl "$(GPU_LAYERS)" \
		-a "$(MODEL_ALIAS)" \
		$(EXTRA_ARGS)

llama-server-large:
	@$(MAKE) llama-server-vulkan \
		LLAMA_SERVER="$(VULKAN_LLAMA_SERVER)" \
		MODEL="$(LARGE_MODEL)" \
		HOST="$(HOST)" \
		PORT="$(PORT)" \
		CTX_SIZE="$(CTX_SIZE)" \
		GPU_LAYERS="$(GPU_LAYERS)" \
		MODEL_ALIAS="$(MODEL_ALIAS)" \
		EXTRA_ARGS="$(EXTRA_ARGS)"

llama-server-cpu:
	@$(MAKE) llama-server \
		LLAMA_SERVER="$(CPU_LLAMA_SERVER)" \
		MODEL="$(CPU_MODEL)" \
		HOST="$(HOST)" \
		PORT="$(PORT)" \
		CTX_SIZE="$(CTX_SIZE)" \
		GPU_LAYERS=0 \
		MODEL_ALIAS="$(MODEL_ALIAS)" \
		EXTRA_ARGS="$(EXTRA_ARGS)"

llama-server-upstream:
	@if [ ! -x "$(LLAMA_SERVER)" ] && ! command -v "$(LLAMA_SERVER)" >/dev/null 2>&1; then \
		printf 'llama-server not found in PATH.\n' >&2; \
		printf 'Set LLAMA_SERVER=/path/to/llama-server or install/download llama.cpp.\n' >&2; \
		exit 1; \
	fi
	@if [ ! -f "$(MODEL)" ]; then \
		printf 'Model file not found: %s\n' "$(MODEL)" >&2; \
		printf 'Pass MODEL=/absolute/path/to/model.gguf\n' >&2; \
		exit 1; \
	fi
	@mkdir -p "$(LOGS_DIR)"
	"$(LLAMA_SERVER)" \
		-m "$(MODEL)" \
		--host "$(UPSTREAM_HOST)" \
		--port "$(UPSTREAM_PORT)" \
		-c "$(CTX_SIZE)" \
		-ngl "$(GPU_LAYERS)" \
		-a "$(MODEL_ALIAS)" \
		-lv 4 \
		--log-prefix \
		--log-timestamps \
		--log-file "$(SERVER_LOG)" \
		$(EXTRA_ARGS)

llama-proxy:
	@if ! "$(PYTHON)" --version >/dev/null 2>&1; then \
		printf 'python not found: %s\n' "$(PYTHON)" >&2; \
		exit 1; \
	fi
	@if [ ! -f "$(PROXY_SCRIPT)" ]; then \
		printf 'Proxy script not found: %s\n' "$(PROXY_SCRIPT)" >&2; \
		exit 1; \
	fi
	@mkdir -p "$(LOGS_DIR)"
	"$(PYTHON)" "$(PROXY_SCRIPT)" \
		--listen-host "$(PROXY_HOST)" \
		--listen-port "$(PROXY_PORT)" \
		--upstream "$(PROXY_UPSTREAM)" \
		--log-file "$(PROXY_LOG)"

llama-stack:
	@if ! "$(PYTHON)" --version >/dev/null 2>&1; then \
		printf 'python not found: %s\n' "$(PYTHON)" >&2; \
		exit 1; \
	fi
	@if [ ! -x "$(LLAMA_SERVER)" ] && ! command -v "$(LLAMA_SERVER)" >/dev/null 2>&1; then \
		printf 'llama-server not found in PATH.\n' >&2; \
		printf 'Set LLAMA_SERVER=/path/to/llama-server or install/download llama.cpp.\n' >&2; \
		exit 1; \
	fi
	@if [ ! -f "$(MODEL)" ]; then \
		printf 'Model file not found: %s\n' "$(MODEL)" >&2; \
		printf 'Pass MODEL=/absolute/path/to/model.gguf\n' >&2; \
		exit 1; \
	fi
	@if [ ! -f "$(PROXY_SCRIPT)" ]; then \
		printf 'Proxy script not found: %s\n' "$(PROXY_SCRIPT)" >&2; \
		exit 1; \
	fi
	@mkdir -p "$(LOGS_DIR)"
	@trap 'jobs -p | xargs -r kill >/dev/null 2>&1' EXIT INT TERM; \
		"$(LLAMA_SERVER)" \
			-m "$(MODEL)" \
			--host "$(UPSTREAM_HOST)" \
			--port "$(UPSTREAM_PORT)" \
			-c "$(CTX_SIZE)" \
			-ngl "$(GPU_LAYERS)" \
			-a "$(MODEL_ALIAS)" \
			-lv 4 \
			--log-prefix \
			--log-timestamps \
			--log-file "$(SERVER_LOG)" \
			$(EXTRA_ARGS) & \
		server_pid=$$!; \
		sleep 2; \
		if ! kill -0 $$server_pid >/dev/null 2>&1; then \
			printf 'llama-server exited early. Check %s\n' "$(SERVER_LOG)" >&2; \
			exit 1; \
		fi; \
		"$(PYTHON)" "$(PROXY_SCRIPT)" \
			--listen-host "$(PROXY_HOST)" \
			--listen-port "$(PROXY_PORT)" \
			--upstream "$(PROXY_UPSTREAM)" \
			--log-file "$(PROXY_LOG)"; \
		wait $$server_pid

llama-health:
	@if ! command -v curl >/dev/null 2>&1; then \
		printf 'curl not found in PATH.\n' >&2; \
		exit 1; \
	fi
	@curl --fail --silent --show-error "http://$(HOST):$(PORT)/v1/models"

llama-health-upstream:
	@if ! command -v curl >/dev/null 2>&1; then \
		printf 'curl not found in PATH.\n' >&2; \
		exit 1; \
	fi
	@curl --fail --silent --show-error "http://$(UPSTREAM_HOST):$(UPSTREAM_PORT)/v1/models"

llama-chat-test:
	@if ! command -v curl >/dev/null 2>&1; then \
		printf 'curl not found in PATH.\n' >&2; \
		exit 1; \
	fi
	@curl --fail --silent --show-error "http://$(HOST):$(PORT)/v1/chat/completions" \
		-H 'Content-Type: application/json' \
		-d '{"model":"$(MODEL_ALIAS)","messages":[{"role":"system","content":"Ты NPC в игре. Отвечай только по-русски и кратко."},{"role":"user","content":"$(TEST_PROMPT)"}],"temperature":0.6,"max_tokens":96,"stream":false}'

voice-backend-install:
	@if ! "$(PYTHON)" --version >/dev/null 2>&1; then \
		printf 'python not found: %s\n' "$(PYTHON)" >&2; \
		exit 1; \
	fi
	@"$(PYTHON)" -m venv "$(VOICE_BACKEND_VENV)"
	@"$(VOICE_BACKEND_PYTHON)" -m pip install --upgrade pip
	@"$(VOICE_BACKEND_PYTHON)" -m pip install -r "$(VOICE_BACKEND_REQUIREMENTS)"

voice-backend:
	@if ! "$(PYTHON)" --version >/dev/null 2>&1; then \
		printf 'python not found: %s\n' "$(PYTHON)" >&2; \
		exit 1; \
	fi
	@if [ ! -x "$(VOICE_BACKEND_PYTHON)" ]; then \
		$(MAKE) voice-backend-install; \
	fi
	@mkdir -p "$(LOGS_DIR)"
	@trap 'jobs -p | xargs -r kill >/dev/null 2>&1' EXIT INT TERM; \
		VOICE_STT_BACKEND="$(VOICE_STT_BACKEND)" \
		VOICE_TTS_BACKEND="$(VOICE_TTS_BACKEND)" \
		VOICE_STT_MODEL="$(VOICE_STT_MODEL)" \
		VOICE_TTS_PIPER_MODEL="$(VOICE_TTS_PIPER_MODEL)" \
		VOICE_TTS_PIPER_CONFIG="$(VOICE_TTS_PIPER_CONFIG)" \
		VOICE_TTS_PIPER_USE_SPEAKER="$(VOICE_TTS_PIPER_USE_SPEAKER)" \
		VOICE_TTS_PIPER_BIN="$(VOICE_TTS_PIPER_BIN)" \
		"$(VOICE_BACKEND_PYTHON)" -m uvicorn app:app --app-dir "$(VOICE_BACKEND_DIR)" --host 127.0.0.1 --port $(VOICE_STT_PORT) & \
		stt_pid=$$!; \
		sleep 1; \
		VOICE_STT_BACKEND="$(VOICE_STT_BACKEND)" \
		VOICE_TTS_BACKEND="$(VOICE_TTS_BACKEND)" \
		VOICE_STT_MODEL="$(VOICE_STT_MODEL)" \
		VOICE_TTS_PIPER_MODEL="$(VOICE_TTS_PIPER_MODEL)" \
		VOICE_TTS_PIPER_CONFIG="$(VOICE_TTS_PIPER_CONFIG)" \
		VOICE_TTS_PIPER_USE_SPEAKER="$(VOICE_TTS_PIPER_USE_SPEAKER)" \
		VOICE_TTS_PIPER_BIN="$(VOICE_TTS_PIPER_BIN)" \
		"$(VOICE_BACKEND_PYTHON)" -m uvicorn app:app --app-dir "$(VOICE_BACKEND_DIR)" --host 127.0.0.1 --port $(VOICE_TTS_PORT) & \
		tts_pid=$$!; \
		wait $$stt_pid $$tts_pid

voice-backend-health:
	@if ! command -v curl >/dev/null 2>&1; then \
		printf 'curl not found in PATH.\n' >&2; \
		exit 1; \
	fi
	@curl --fail --silent --show-error "http://127.0.0.1:$(PORT)/health"
