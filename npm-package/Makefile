windowsDir := ../npm-package/binaries/windows
osxDir := ../npm-package/binaries/osx
linuxDir := ../npm-package/binaries/linux

default: windows osx-intel osx-arm linux linux-arm pack

pack: build
	npm pack

build:
	npm run build 

windows:
	dotnet publish ../src -r win-x64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishDir=$(windowsDir)

osx-intel:
	dotnet publish ../src -r osx-x64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishDir=$(osxDir)/osx-x64

osx-arm:
	dotnet publish ../src -r osx-arm64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishDir=$(osxDir)/osx-arm64

linux:
	dotnet publish ../src -r linux-x64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishDir=$(linuxDir)/linux-x64

linux-arm:
	dotnet publish ../src -r linux-arm64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishDir=$(linuxDir)/linux-arm64