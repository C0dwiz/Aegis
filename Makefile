# Aegis stack Makefile

.PHONY: help build run stop logs clean status test

# Default target
help:
	@echo "Aegis Docker Stack Commands:"
	@echo "======================================"
	@echo "make build    - Build all docker images"
	@echo "make run      - Run postgres + server + bot api"
	@echo "make stop     - Stop server"
	@echo "make logs     - Show stack logs"
	@echo "make status   - Show container status"
	@echo "make clean    - Remove containers, images and volume"
	@echo "make test     - Test connection to server"

# Build Docker image
build:
	@echo "Building docker images..."
	docker compose build

# Run server and show connection info
run: build
	@echo "Starting docker stack..."
	docker compose up -d
	@echo "TCP: localhost:8888"
	@echo "Bot API: http://localhost:5000"

# Stop server
stop:
	@echo "Stopping docker stack..."
	docker compose down

# Show logs
logs:
	@echo "Stack logs:"
	@echo "==============="
	docker compose logs -f

# Show container status
status:
	@echo "Container status:"
	@echo "===================="
	docker compose ps

# Clean up containers and images
clean:
	@echo "Cleaning up..."
	docker compose down -v --remove-orphans
	docker image rm aegis-aegis-server aegis-aegis-botapi 2>/dev/null || true

# Test connection to server
test:
	@echo "Testing connection..."
	@if nc -z localhost 8888 2>/dev/null; then \
		echo "Server is reachable on localhost:8888"; \
	else \
		echo "Server is not reachable on localhost:8888"; \
		echo "Try running 'make run' first"; \
	fi
