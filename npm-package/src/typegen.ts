#!/usr/bin/env node

import { spawn } from 'child_process';
import * as path from 'path';
import * as fs from 'fs';
import { fileURLToPath } from 'url';

// ES module equivalent of __dirname
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

function getExecutablePath(): string {
    const platform = process.platform;
    const arch = process.arch;
    const packageDir = path.dirname(__dirname);

    let executablePath: string;

    switch (platform) {
        case 'win32':
            executablePath = path.join(packageDir, 'binaries', 'windows', 'typegen.exe');
            break;
        case 'darwin':
            if (arch === 'arm64') {
                executablePath = path.join(packageDir, 'binaries', 'osx-arm64', 'typegen');
            } else {
                executablePath = path.join(packageDir, 'binaries', 'osx-x64', 'typegen');
            }
            break;
        case 'linux':
            if (arch === 'arm64') {
                executablePath = path.join(packageDir, 'binaries', 'linux-arm64', 'typegen');
            } else {
                executablePath = path.join(packageDir, 'binaries', 'linux-x64', 'typegen');
            }
            break;
        default:
            console.error(`Unsupported platform: ${platform}-${arch}`);
            process.exit(1);
    }

    // Check if executable exists
    if (!fs.existsSync(executablePath)) {
        console.error(`Executable not found: ${executablePath}`);
        console.error(`Platform: ${platform}-${arch}`);
        console.error('Make sure @cakeru/typegen is installed globally with: npm install @cakeru/typegen -g');
        process.exit(1);
    }

    return executablePath;
}

function runExecutable(): void {
    const executablePath = getExecutablePath();
    const args = process.argv.slice(2);

    // Spawn the platform-specific executable
    const child = spawn(executablePath, args, {
        stdio: 'inherit',
        shell: false
    });

    // Handle process exit
    child.on('close', (code) => {
        process.exit(code || 0);
    });

    child.on('error', (error) => {
        console.error('Failed to start typegen:', error.message);
        console.error('Make sure @cakeru/typegen is installed globally with: npm install @cakeru/typegen -g');
        process.exit(1);
    });

    // Handle SIGINT (Ctrl+C)
    process.on('SIGINT', () => {
        child.kill('SIGINT');
    });

    // Handle SIGTERM
    process.on('SIGTERM', () => {
        child.kill('SIGTERM');
    });
}

// Check if this is a global installation
const isGlobal = __dirname.includes('node_modules') &&
    (__dirname.includes('/usr/') ||
        __dirname.includes('\\AppData\\') ||
        __dirname.includes('/.npm/') ||
        process.env.npm_config_global === 'true');

if (!isGlobal) {
    console.error('Error: @cakeru/typegen must be installed globally.');
    console.error('Please run: npm install @cakeru/typegen -g');
    process.exit(1);
}

runExecutable();