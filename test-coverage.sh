#!/bin/bash

# Script to run tests with code coverage
set -e

echo "🧪 Running tests with code coverage..."

# Clean previous coverage results
rm -rf artifacts/coverage/
mkdir -p artifacts/coverage

# Run unit tests with coverage
echo "📊 Running unit tests with coverage..."
dotnet test test/UnitTests/UnitTests.csproj \
  --collect:"XPlat Code Coverage" \
  --results-directory:artifacts/coverage \
  --logger:trx \
  --verbosity normal

# Run integration tests with coverage
echo "🔧 Running integration tests with coverage..."
dotnet test test/IntegrationTests/IntegrationTests.csproj \
  --collect:"XPlat Code Coverage" \
  --results-directory:artifacts/coverage \
  --logger:trx \
  --verbosity normal

# Merge coverage reports if multiple exist
echo "📈 Processing coverage results..."

# Find all coverage files
coverage_files=$(find artifacts/coverage -name "*.xml" -type f)
coverage_count=$(echo "$coverage_files" | wc -l)

if [ $coverage_count -gt 0 ]; then
  echo "✅ Found $coverage_count coverage file(s)"

  # Install reportgenerator if not already installed
  if ! command -v reportgenerator &> /dev/null; then
    echo "📦 Installing ReportGenerator..."
    dotnet tool install -g dotnet-reportgenerator-globaltool
  fi

  # Generate HTML report
  echo "📄 Generating HTML coverage report..."
  reportgenerator \
    -reports:"artifacts/coverage/**/coverage.cobertura.xml" \
    -targetdir:artifacts/coverage/html \
    -reporttypes:Html \
    -verbosity:Info

  # Generate summary
  reportgenerator \
    -reports:"artifacts/coverage/**/coverage.cobertura.xml" \
    -targetdir:artifacts/coverage \
    -reporttypes:TextSummary \
    -verbosity:Info

  echo ""
  echo "📊 Coverage Summary:"
  echo "==================="
  if [ -f "artifacts/coverage/Summary.txt" ]; then
    cat artifacts/coverage/Summary.txt
  fi

  echo ""
  echo "🌐 HTML Report: artifacts/coverage/html/index.html"
  echo "📁 Coverage Files: artifacts/coverage/"
else
  echo "❌ No coverage files found"
  exit 1
fi
