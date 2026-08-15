#!/usr/bin/env bash
#
# Publishes a new version: raises the number, builds the Windows executable,
# tags it, and puts it on GitHub as a release. The app compares itself against
# whatever this leaves as the latest release.
#
#   ./release.sh 1.1.0 "Poupa papel e traz teclado próprio"
#
set -euo pipefail

VERSION="${1:-}"
NOTES="${2:-}"

if [[ -z "$VERSION" ]]; then
  echo "Uso: ./release.sh <versão> [notas]"
  echo "     ./release.sh 1.1.0 \"Poupa papel e traz teclado próprio\""
  exit 1
fi

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "A versão tem de ser no formato 1.2.3 — recebi '$VERSION'"
  exit 1
fi

cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH"

# Publishing from a dirty tree would ship something that is in no commit.
if [[ -n "$(git status --porcelain)" ]]; then
  echo "Há alterações por confirmar. Faça commit antes de publicar uma versão."
  git status --short
  exit 1
fi

if git rev-parse "v$VERSION" >/dev/null 2>&1; then
  echo "A versão v$VERSION já existe."
  exit 1
fi

echo "==> Versão $VERSION"
# The number the app reads at runtime lives in Directory.Build.props.
sed -i '' "s|<Version>.*</Version>|<Version>$VERSION</Version>|" Directory.Build.props

echo "==> Testes"
dotnet test --nologo | tail -3

echo "==> Executável para Windows"
rm -rf publish
dotnet publish src/SellingPoint.App -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/win-x64 --nologo | tail -2

test -f publish/win-x64/SenhasDoCalvario.exe

echo "==> Commit e etiqueta"
git add Directory.Build.props
# Já pode estar no número pedido — a primeira versão, ou uma repetição depois de
# um erro. Nesse caso não há nada a confirmar e seguimos para a etiqueta.
if git diff --cached --quiet; then
  echo "    (Directory.Build.props já está em $VERSION)"
else
  git commit -q -m "Version $VERSION"
fi
git tag "v$VERSION"
git push --quiet
git push --quiet origin "v$VERSION"

echo "==> Publicar no GitHub"
gh release create "v$VERSION" publish/win-x64/SenhasDoCalvario.exe \
  --title "v$VERSION" \
  --notes "${NOTES:-Sem notas.}"

echo
echo "Publicada a v$VERSION."
echo "As cópias instaladas vão encontrá-la em Definições → Procurar atualização."
