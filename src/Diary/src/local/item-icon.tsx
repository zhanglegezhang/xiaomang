type ItemIconProps = { folder: boolean; path?: string; size: number; color?: string; solid?: boolean }

// Solid shapes include their own full-size border and transparent details.
// Adding the outline stroke to a filled shape would swallow those details at 14px.
const folderSolid = 'M3 2H9.2L12.2 5H21A3 3 0 0 1 24 8V20A3 3 0 0 1 21 23H3A3 3 0 0 1 0 20V5A3 3 0 0 1 3 2Z M2.5 8H21.5V10H2.5Z';
const fileSolid = 'M5.5 .5H13.5L21.5 8.5V20.5A3 3 0 0 1 18.5 23.5H5.5A3 3 0 0 1 2.5 20.5V3.5A3 3 0 0 1 5.5 .5Z M14.5 3V7.5H19Z';
const markdownDetail = 'M6.5 19V11.5H8L12 15.5L16 11.5H17.5V19H16V13.75L12 17.75L8 13.75V19Z';
const textDetail = 'M7 11H17V13H7Z M7 15H17V17H7Z M7 19H14V21H7Z';

export default function ItemIcon({ folder, path = '', size, color, solid = false }: ItemIconProps) {
  const text = /\.txt$/i.test(path);
  return <svg className="item-icon" data-kind={folder ? 'folder' : 'file'} data-format={folder ? undefined : text ? 'txt' : 'md'} data-solid={solid ? 'true' : undefined} width={size} height={size} viewBox="0 0 24 24" fill={solid ? 'currentColor' : 'none'} stroke={solid ? 'none' : 'currentColor'} strokeWidth={solid ? undefined : folder ? 3 : 2} strokeLinecap="round" strokeLinejoin="round" style={{ color }} aria-hidden="true" focusable="false">
    {solid
      ? <path fillRule="evenodd" clipRule="evenodd" d={folder ? folderSolid : `${fileSolid} ${text ? textDetail : markdownDetail}`} />
      : folder
        ? <path d="M4 4h5.2c.5 0 1 .2 1.3.6L12 6h8a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z" />
        : <><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z M14 2v6h6" /><path data-detail={text ? 'text-lines' : 'markdown-m'} stroke="none" fill="currentColor" d={text ? textDetail : markdownDetail} /></>}
  </svg>
}
