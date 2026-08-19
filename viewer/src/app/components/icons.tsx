import type React from 'react';

type IconProps = React.SVGProps<SVGSVGElement>;

function Icon({ children, ...props }: IconProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      width="1em"
      height="1em"
      {...props}
    >
      {children}
    </svg>
  );
}

export function IconSearch(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="11" cy="11" r="7" />
      <path d="M20 20l-3.5-3.5" />
    </Icon>
  );
}

export function IconSettings(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="12" cy="12" r="3" />
      <path d="M12 3.5v2.2M12 18.3v2.2M5.6 5.6l1.6 1.6M16.8 16.8l1.6 1.6M3.5 12h2.2M18.3 12h2.2M5.6 18.4l1.6-1.6M16.8 7.2l1.6-1.6" />
    </Icon>
  );
}

export function IconHelp(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="M9.5 9.5a2.5 2.5 0 1 1 3.4 2.3c-.8.4-1.4 1.1-1.4 2v.2" />
      <circle cx="12" cy="17" r="0.6" fill="currentColor" stroke="none" />
    </Icon>
  );
}

export function IconFolder(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M3 7.5A1.5 1.5 0 0 1 4.5 6H9l1.8 1.8H19.5A1.5 1.5 0 0 1 21 9.3v7.2a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 16.5z" />
    </Icon>
  );
}

export function IconPlay(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M8 6.5v11l9-5.5z" fill="currentColor" stroke="none" />
    </Icon>
  );
}

export function IconRefresh(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M20 12a8 8 0 1 1-2.2-5.5" />
      <path d="M20 4v5h-5" />
    </Icon>
  );
}

export function IconTrash(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M4 7h16" />
      <path d="M9 7V5.5A1.5 1.5 0 0 1 10.5 4h3A1.5 1.5 0 0 1 15 5.5V7" />
      <path d="M6.5 7l.7 12.2A1.5 1.5 0 0 0 8.7 20.5h6.6a1.5 1.5 0 0 0 1.5-1.3L17.5 7" />
      <path d="M10 11v6M14 11v6" />
    </Icon>
  );
}

export function IconKey(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="8" cy="15" r="3.5" />
      <path d="M11 15h10v2.5M17.5 15v2.5" />
    </Icon>
  );
}

export function IconShuffle(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M16 3h5v5" />
      <path d="M4 20L21 3" />
      <path d="M21 16v5h-5" />
      <path d="M15 15l6 6" />
      <path d="M4 4l5 5" />
    </Icon>
  );
}

export function IconClock(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="12" cy="12" r="8.5" />
      <path d="M12 7.5V12l3 2" />
    </Icon>
  );
}

export function IconPeople(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="9" cy="8" r="2.5" />
      <path d="M3.5 19.5v-.8A5.5 5.5 0 0 1 9 13.2a5.5 5.5 0 0 1 5.5 5.5v.8" />
      <circle cx="17" cy="8.5" r="2" />
      <path d="M16 13.5a4.2 4.2 0 0 1 4.5 4.2v1.8" />
    </Icon>
  );
}

export function IconTerminal(props: IconProps) {
  return (
    <Icon {...props}>
      <rect x="3" y="5" width="18" height="14" rx="2" />
      <path d="M7 10l3 2-3 2" />
      <path d="M13 14h4" />
    </Icon>
  );
}

export function IconClose(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="M9 9l6 6M15 9l-6 6" />
    </Icon>
  );
}

export function IconPlus(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M12 5v14M5 12h14" />
    </Icon>
  );
}

export function IconMinus(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M5 12h14" />
    </Icon>
  );
}

export function IconChevronLeft(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M14.5 5.5L8 12l6.5 6.5" />
    </Icon>
  );
}

export function IconChevronRight(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M9.5 5.5L16 12l-6.5 6.5" />
    </Icon>
  );
}

export function IconGlobe(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="12" cy="12" r="8.5" />
      <path d="M3.5 12h17" />
      <path d="M12 3.5c2.4 2.4 3.6 5.4 3.6 8.5s-1.2 6.1-3.6 8.5c-2.4-2.4-3.6-5.4-3.6-8.5s1.2-6.1 3.6-8.5z" />
    </Icon>
  );
}

export function IconMap(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M3.5 6.5l5.5-2 6 2 5.5-2v13l-5.5 2-6-2-5.5 2z" />
      <path d="M9 4.5v13M15 6.5v13" />
    </Icon>
  );
}

export function IconTimeline(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M4 16.5l4.2-6.2 3.2 3.4 4.8-8.2 3.8 4.6" />
    </Icon>
  );
}

export function IconPerson(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="12" cy="8" r="2.6" />
      <path d="M5.5 19.5v-.6A6.5 6.5 0 0 1 12 12.4a6.5 6.5 0 0 1 6.5 6.5v.6" />
    </Icon>
  );
}

export function IconSwords(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M14.2 4.8l5 5-8.7 8.7H6.2v-4.3z" />
      <path d="M12 8.2l3.8 3.8" />
      <path d="M4.8 14.2l5 5" />
    </Icon>
  );
}

export function IconCity(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M4 20V9l4-2v13M8 20V7l6-3v16M14 20v-8h6v8" />
      <path d="M16.5 14v.01M18.5 14v.01M16.5 16.5v.01M18.5 16.5v.01" />
    </Icon>
  );
}

export function IconRoute(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="6.5" cy="6.5" r="2" />
      <circle cx="17.5" cy="17.5" r="2" />
      <path d="M8.4 7.8c3.2 0 3.2 8.4 7.2 8.4" />
    </Icon>
  );
}

export function IconCrown(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M4 16.5l2.5-8 3.5 4 2-6 2 6 3.5-4 2.5 8z" />
      <path d="M5 19h14" />
    </Icon>
  );
}

export function IconFaith(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M12 3.5l1.6 5.2h5.4l-4.4 3.2 1.7 5.3L12 14.2 7.7 17.2l1.7-5.3-4.4-3.2h5.4z" />
    </Icon>
  );
}

export function IconLandmark(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M4 20h16" />
      <path d="M12 3.5L5.5 8h13z" />
      <path d="M7.5 8v8M12 8v8M16.5 8v8" />
    </Icon>
  );
}

export function IconGem(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M7 5.5h10l3.5 5.5L12 20 3.5 11z" />
      <path d="M3.5 11h17M7 5.5L12 11 17 5.5" />
    </Icon>
  );
}

export function IconDrop(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M12 3.5c3.6 4.6 6 8 6 11.2A6 6 0 0 1 6 14.7C6 11.5 8.4 8.1 12 3.5z" />
    </Icon>
  );
}

export function IconBolt(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M13 3.5L6.5 13h5.2L11 20.5 17.5 11h-5.2z" />
    </Icon>
  );
}

export function IconFlag(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M6 4v16" />
      <path d="M6 5h11l-2.2 3.8L17 12.5H6" />
    </Icon>
  );
}

export function IconStar(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="12" cy="12" r="3.2" />
      <path d="M12 3v2.2M12 18.8V21M4.8 4.8l1.6 1.6M17.6 17.6l1.6 1.6M3 12h2.2M18.8 12H21M4.8 19.2l1.6-1.6M17.6 6.4l1.6-1.6" />
    </Icon>
  );
}

export function IconHex(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M12 3.5l7 4v9l-7 4-7-4v-9z" />
    </Icon>
  );
}
