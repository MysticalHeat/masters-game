SHELL := /usr/bin/env bash

.DEFAULT_GOAL := help

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

.PHONY: help llama-server llama-server-vulkan llama-server-cpu llama-server-large llama-health llama-chat-test

help:
	@printf '%s\n' \
		'Targets:' \
		'  make llama-server [MODEL=/abs/path/to/model.gguf PORT=8080 CTX_SIZE=4096 GPU_LAYERS=999 MODEL_ALIAS=qwen]' \
		'  make llama-server-vulkan [MODEL=/abs/path/to/model.gguf PORT=8080 CTX_SIZE=4096 GPU_LAYERS=999 MODEL_ALIAS=qwen]' \
		'  make llama-server-cpu [MODEL=/abs/path/to/model.gguf PORT=8080 CTX_SIZE=4096 MODEL_ALIAS=qwen]' \
		'  make llama-server-large [PORT=8080 CTX_SIZE=4096 GPU_LAYERS=999 MODEL_ALIAS=qwen]' \
		'  make llama-health [HOST=127.0.0.1 PORT=8080]' \
		'  make llama-chat-test [HOST=127.0.0.1 PORT=8080 MODEL_ALIAS=qwen TEST_PROMPT="..."]' \
		'' \
		'Defaults:' \
		'  LLAMA_SERVER       -> tools/llama.cpp/llama-b8913-vulkan/llama-server' \
		'  llama-server      -> Vulkan + Assets/Models/Qwen/qwen2.5-3b-instruct-q4_k_m.gguf' \
		'  llama-server-large -> Vulkan + Assets/Models/Qwen/qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf' \
		'  llama-server-cpu -> Assets/Models/Qwen/qwen2.5-3b-instruct-q4_k_m.gguf' \
		'' \
		'Laptop note (Core Ultra 9 + Intel Arc iGPU):' \
		'  recommended default is make llama-server (Vulkan + 3B)' \
		'' \
		'Examples:' \
		'  make llama-server' \
		'  make llama-server-large' \
		'  make llama-server-cpu' \
		'  make llama-health' \
		'  make llama-chat-test' \
		'  make llama-server MODEL=/models/custom.gguf'

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

llama-health:
	@if ! command -v curl >/dev/null 2>&1; then \
		printf 'curl not found in PATH.\n' >&2; \
		exit 1; \
	fi
	@curl --fail --silent --show-error "http://$(HOST):$(PORT)/v1/models"

llama-chat-test:
	@if ! command -v curl >/dev/null 2>&1; then \
		printf 'curl not found in PATH.\n' >&2; \
		exit 1; \
	fi
	@curl --fail --silent --show-error "http://$(HOST):$(PORT)/v1/chat/completions" \
		-H 'Content-Type: application/json' \
		-d '{"model":"$(MODEL_ALIAS)","messages":[{"role":"system","content":"Ты NPC в игре. Отвечай только по-русски и кратко."},{"role":"user","content":"$(TEST_PROMPT)"}],"temperature":0.6,"max_tokens":96,"stream":false}'
