#!/bin/bash

# Aegis Server Docker Startup Script
# This script starts the Aegis server and displays connection information

set -e

echo "🚀 Starting Aegis Messenger Server..."

# Check if Docker is running
if ! docker info > /dev/null 2>&1; then
    echo "❌ Docker is not running. Please start Docker first."
    exit 1
fi

# Build and start containers
echo "📦 Building and starting containers..."
docker-compose -f docker-compose.yaml up --build -d

# Wait for server to start
echo "⏳ Waiting for server to start..."
sleep 10

# Get container IP and display connection info
echo "🌐 Server Connection Information:"
echo "=================================="

# Get server container IP
SERVER_IP=$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' aegis-messenger-server)

if [ -z "$SERVER_IP" ]; then
    echo "❌ Could not get server IP address"
    echo "📋 Using localhost for connection"
    SERVER_IP="localhost"
fi

echo "📍 Server IP: $SERVER_IP"
echo "🔌 Port: 8888"
echo "🔗 Connection URL: $SERVER_IP:8888"

# Display container status
echo ""
echo "📊 Container Status:"
echo "===================="
docker-compose -f docker-compose.yaml ps

# Display logs
echo ""
echo "📋 Recent Server Logs:"
echo "======================"
docker logs aegis-messenger-server --tail 20

# Test connection
echo ""
echo "🧪 Testing Connection..."
echo "========================="
if command -v nc &> /dev/null; then
    if nc -z $SERVER_IP 8888 &> /dev/null; then
        echo "✅ Server is reachable on $SERVER_IP:8888"
    else
        echo "❌ Server is not reachable on $SERVER_IP:8888"
    fi
else
    echo "⚠️  netcat not available, cannot test connection"
fi

echo ""
echo "🎯 Dart Client Connection Example:"
echo "================================="
echo "import 'package:aegis_client/aegis_client.dart';"
echo ""
echo "final client = AegisClient();"
echo "await client.connect('$SERVER_IP', 8888);"
echo "await client.authenticate('your_token');"
echo "await client.sendMessage('Hello from Docker!');"

echo ""
echo "🛑 To stop server: docker-compose -f docker-compose.yaml down"
echo "📝 To view logs: docker logs aegis-messenger-server -f"
