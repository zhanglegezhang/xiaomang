import type { NextConfig } from 'next'
const config: NextConfig = { output: 'export', images: { unoptimized: true }, reactStrictMode: false, eslint: { ignoreDuringBuilds: true } }
export default config
