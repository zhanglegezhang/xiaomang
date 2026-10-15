export type TableOperation = 'row-before' | 'row-after' | 'column-before' | 'column-after' | 'delete-row' | 'delete-column';

export default function TableOperationIcon({ operation }: { operation: TableOperation }) {
  const row = operation.includes('row');
  const deleting = operation.startsWith('delete');
  const position = deleting ? 11 : operation.endsWith('before') ? 5.5 : 16.5;
  const badge = deleting ? 'M17 20H23' : 'M17 20H23M20 17V23';
  return <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    <rect x="3" y="3" width="16" height="16" rx="1"/>
    <path d="M3 8H19M3 14H19M8 3V19M14 3V19"/>
    <path d={row ? `M3 ${position}H19` : `M${position} 3V19`} strokeWidth="2.5"/>
    <path d={badge} stroke="white" strokeWidth="5"/>
    <path d={badge} strokeWidth="2"/>
  </svg>;
}
