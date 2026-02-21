# Aegis Server Docker Makefile

.PHONY: help build run stop logs clean status test

# Default target
help:
	@echo "Aegis Messenger Server Docker Commands:"
	@echo "======================================"
	@echo "make build    - Build Docker image"
	@echo "make run      - Run server and show connection info"
	@echo "make stop     - Stop server"
	@echo "make logs     - Show server logs"
	@echo "make status   - Show container status"
	@echo "make clean    - Remove containers and images"
	@echo "make test     - Test connection to server"

# Build Docker image
build:
	@echo "📦 Building Aegis Server image..."
	docker build -f Dockerfile.simple -t aegis-server ../git/Aegis

# Run server and show connection info
run: build
	@echo "🚀 Starting Aegis Server..."
	docker run -d --name aegis-server -p 8888:8888 --restart unless-stopped aegis-server
	@sleep 5
	@echo "🌐 Connection Information:"
	@echo "========================"
	@echo "📍 Localhost: localhost:8888"
	@if command -v docker-ip >/dev/null 2>&1; then \
		IP=$$(docker-ip aegis-server); \
		echo "🔗 Container IP: $$IP:8888"; \
	fi
	@echo "🎯 Dart Client Example:"
	@echo "===================="
	@echo "final client = AegisClient();"
	@echo "await client.connect('localhost', 8888);"
	@echo "await client.authenticate('token');"
	@echo "await client.sendMessage('Hello!');"

# Stop server
stop:
	@echo "🛑 Stopping Aegis Server..."
	docker stop aegis-server || true
	docker rm aegis-server || true

# Show logs
logs:
	@echo "📋 Server Logs:"
	@echo "==============="
	docker logs aegis-server -f

# Show container status
status:
	@echo "📊 Container Status:"
	@echo "===================="
	docker ps -a --filter name=aegis-server

# Clean up containers and images
clean:
	@echo "🧹 Cleaning up..."
	docker stop aegis-server || true
	docker rm aegis-server || true
	docker rmi aegis-server || true
	docker system prune -f

# Test connection to server
test:
	@echo "🧪 Testing connection..."
	@if nc -z localhost 8888 2>/dev/null; then \
		echo "✅ Server is reachable on localhost:8888"; \
	else \
		echo "❌ Server is not reachable on localhost:8888"; \
		echo "💡 Try running 'make run' first"; \
	fi
