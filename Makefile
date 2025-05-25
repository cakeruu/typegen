windowsDir := ../Build/Windows
osxDir := ../Build/OSX
linuxDir := ../Build/Linux

OS ?= $(shell uname -s)

ifeq ($(OS),Windows_NT)
	targetDir := $(windowsDir)
	targetRuntime := win-x64
endif
ifeq ($(OS),Darwin)
	targetDir := $(osxDir)
	targetRuntime := osx-x64
	ifneq (,$(findstring arm,$(shell uname -m)))
		targetRuntime := osx-arm64
	endif
endif
ifeq ($(OS),Linux)
	targetDir := $(linuxDir)
	targetRuntime := linux-x64
	ifneq (,$(findstring arm,$(shell uname -m)))
		targetRuntime := linux-arm64
	endif
endif

default:
	dotnet publish ./src -r $(targetRuntime) -c Release --self-contained true -p:PublishSingleFile=true -p:PublishDir=$(targetDir)

all: windows osx-intel osx-arm linux linux-arm

windows:
	dotnet publish ./src -r win-x64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishDir=$(windowsDir)

osx-intel:
	dotnet publish ./src -r osx-x64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishDir=$(osxDir)

osx-arm:
	dotnet publish ./src -r osx-arm64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishDir=$(osxDir)

linux:
	dotnet publish ./src -r linux-x64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishDir=$(linuxDir)

linux-arm:
	dotnet publish ./src -r linux-arm64 -c Release --self-contained true -p:PublishSingleFile=true -p:PublishDir=$(linuxDir)