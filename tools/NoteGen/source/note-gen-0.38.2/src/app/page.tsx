'use client'
import dynamic from 'next/dynamic'
const Diary = dynamic(() => import('@/local/diary'), { ssr: false })
export default function Page() { return <Diary /> }
