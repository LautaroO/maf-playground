#!/usr/bin/env python3
"""Generate the deterministic PDF used by the Basic RAG example."""

from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import inch
from reportlab.platypus import (
    KeepTogether,
    PageBreak,
    Paragraph,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
)


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "documents" / "help.pdf"

NAVY = colors.HexColor("#17324D")
BLUE = colors.HexColor("#2E6F9E")
PALE_BLUE = colors.HexColor("#EAF3F8")
PALE_GOLD = colors.HexColor("#FFF6DD")
TEXT = colors.HexColor("#243442")
MUTED = colors.HexColor("#627786")
LINE = colors.HexColor("#CBD9E2")


def build_styles():
    styles = getSampleStyleSheet()
    styles.add(ParagraphStyle(
        name="GuideTitle",
        parent=styles["Title"],
        fontName="Helvetica-Bold",
        fontSize=25,
        leading=30,
        textColor=NAVY,
        spaceAfter=8,
    ))
    styles.add(ParagraphStyle(
        name="GuideSubtitle",
        parent=styles["Normal"],
        fontName="Helvetica",
        fontSize=11,
        leading=16,
        textColor=MUTED,
        spaceAfter=20,
    ))
    styles.add(ParagraphStyle(
        name="SectionTitle",
        parent=styles["Heading1"],
        fontName="Helvetica-Bold",
        fontSize=19,
        leading=23,
        textColor=NAVY,
        spaceAfter=14,
    ))
    styles.add(ParagraphStyle(
        name="Topic",
        parent=styles["Heading2"],
        fontName="Helvetica-Bold",
        fontSize=13,
        leading=17,
        textColor=BLUE,
        spaceBefore=7,
        spaceAfter=5,
    ))
    styles.add(ParagraphStyle(
        name="BodyCopy",
        parent=styles["BodyText"],
        fontName="Helvetica",
        fontSize=10.5,
        leading=15.5,
        textColor=TEXT,
        spaceAfter=9,
    ))
    styles.add(ParagraphStyle(
        name="Fact",
        parent=styles["BodyText"],
        fontName="Helvetica-Bold",
        fontSize=10.5,
        leading=15,
        textColor=NAVY,
    ))
    styles.add(ParagraphStyle(
        name="Footer",
        parent=styles["Normal"],
        fontName="Helvetica",
        fontSize=8,
        textColor=MUTED,
        alignment=TA_CENTER,
    ))
    return styles


STYLES = build_styles()


def fact_box(text, background=PALE_BLUE):
    table = Table([[Paragraph(text, STYLES["Fact"]) ]], colWidths=[6.65 * inch])
    table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, -1), background),
        ("BOX", (0, 0), (-1, -1), 0.7, LINE),
        ("LEFTPADDING", (0, 0), (-1, -1), 12),
        ("RIGHTPADDING", (0, 0), (-1, -1), 12),
        ("TOPPADDING", (0, 0), (-1, -1), 9),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 9),
    ]))
    return KeepTogether([table, Spacer(1, 10)])


def topic(title, body):
    return KeepTogether([
        Paragraph(title, STYLES["Topic"]),
        Paragraph(body, STYLES["BodyCopy"]),
    ])


def draw_page(canvas, document):
    canvas.saveState()
    width, height = letter
    canvas.setFillColor(NAVY)
    canvas.rect(0, height - 0.34 * inch, width, 0.34 * inch, fill=1, stroke=0)
    canvas.setStrokeColor(LINE)
    canvas.line(0.75 * inch, 0.56 * inch, width - 0.75 * inch, 0.56 * inch)
    footer = f"Northstar Help Center - Customer Guide  |  Page {document.page}"
    canvas.setFont("Helvetica", 8)
    canvas.setFillColor(MUTED)
    canvas.drawCentredString(width / 2, 0.34 * inch, footer)
    canvas.restoreState()


def build_story():
    story = [
        Paragraph("Northstar Help Center", STYLES["GuideTitle"]),
        Paragraph("Customer Guide - Sample knowledge base document", STYLES["GuideSubtitle"]),
        fact_box(
            "Document ID: NS-HC-2026-01 &nbsp;&nbsp;|&nbsp;&nbsp; Edition: August 2026 &nbsp;&nbsp;|&nbsp;&nbsp; "
            "This is fictional sample content for local RAG testing.",
            PALE_GOLD,
        ),
        Paragraph("1. Accounts and support", STYLES["SectionTitle"]),
        topic(
            "Contacting support",
            "Submit requests through the Northstar customer portal. Standard support hours are "
            "Monday through Friday, 09:00 to 17:00 UTC, excluding company holidays. A standard "
            "support ticket receives an initial response within two business days.",
        ),
        fact_box("TEST FACT: Standard support operates Monday-Friday, 09:00-17:00 UTC. The initial response target is two business days."),
        topic(
            "Updating your profile",
            "Open Settings, choose Profile, update the fields, and select Save changes. Display-name "
            "changes are visible immediately. A primary email-address change requires confirmation "
            "from the new address within 24 hours.",
        ),
        topic(
            "Closing an account",
            "An account owner can request closure under Settings > Account > Close account. Northstar "
            "holds the account in a recoverable state for 14 calendar days. After that period, closure "
            "becomes permanent and normal sign-in is disabled.",
        ),
        fact_box("TEST FACT: Account closure has a 14-calendar-day recovery window."),
        PageBreak(),

        Paragraph("2. Passwords and account security", STYLES["SectionTitle"]),
        topic(
            "Resetting a forgotten password",
            "On the sign-in page, select Forgot password, enter the primary email address, and use the "
            "single-use link sent by Northstar. The reset link expires 30 minutes after it is issued. "
            "Requesting another link immediately invalidates the previous one.",
        ),
        fact_box("TEST FACT: Password-reset links are single-use and expire after 30 minutes."),
        topic(
            "Failed sign-in protection",
            "Five consecutive failed sign-in attempts lock the account for 15 minutes. The failed-attempt "
            "counter resets after a successful sign-in. Support cannot shorten the automatic lockout period.",
        ),
        fact_box("TEST FACT: Five failed attempts trigger a 15-minute lockout."),
        topic(
            "Multi-factor authentication",
            "Northstar supports authenticator applications using time-based one-time passwords. Enabling "
            "multi-factor authentication generates 10 single-use recovery codes. Each recovery code can "
            "be used once; generating a new set invalidates every unused code from the old set.",
        ),
        topic(
            "Suspicious sessions",
            "Review active sessions under Settings > Security > Sessions. Selecting Revoke all other "
            "sessions signs out every device except the current one within five minutes.",
        ),
        PageBreak(),

        Paragraph("3. Billing, plans, and refunds", STYLES["SectionTitle"]),
        topic(
            "Invoices and payment dates",
            "Monthly invoices are generated on the first calendar day of each month. Payment is due 14 "
            "calendar days after the invoice date. Invoices are available as PDF files under Billing > Invoices.",
        ),
        fact_box("TEST FACT: Monthly invoices are generated on day 1 and are due 14 calendar days later."),
        topic(
            "Plan changes",
            "Upgrades take effect immediately and are prorated for the remaining billing period. Downgrades "
            "take effect at the start of the next billing period. Changing a plan does not alter existing "
            "invoice due dates.",
        ),
        topic(
            "Refund eligibility",
            "A first-time subscription may be refunded when the account owner submits the request within "
            "seven calendar days of the initial purchase. Renewal charges and usage-based charges are not "
            "eligible for this sample policy. Approved refunds return to the original payment method.",
        ),
        fact_box("TEST FACT: The first-purchase refund request window is seven calendar days."),
        topic(
            "Billing record retention",
            "Invoices and payment receipts remain available in the portal for 24 months. Account owners "
            "should download older records before they leave the online retention window.",
        ),
        PageBreak(),

        Paragraph("4. Service operations and data", STYLES["SectionTitle"]),
        topic(
            "Incident priorities",
            "Northstar classifies incidents as P1 Critical, P2 High, P3 Normal, or P4 Low. A P1 incident "
            "means the production service is unavailable to all users or a confirmed active data-loss event "
            "is occurring. The P1 initial-response target is 30 minutes at any time of day.",
        ),
        fact_box("TEST FACT: P1 Critical incidents have a 30-minute initial-response target, 24 hours a day."),
        topic(
            "Backups",
            "The service creates a database backup every day at 02:00 UTC. Daily backups are retained for "
            "30 calendar days. This sample guide does not promise point-in-time recovery between daily backups.",
        ),
        fact_box("TEST FACT: Backups run daily at 02:00 UTC and are retained for 30 calendar days."),
        topic(
            "Exporting workspace data",
            "An account owner can request an export under Settings > Data export. Northstar prepares exports "
            "in JSON and CSV formats. The download link remains valid for 72 hours after the export is ready.",
        ),
        topic(
            "Questions this guide does not answer",
            "This document does not specify product pricing, physical office locations, telephone support "
            "numbers, regulatory certifications, or future roadmap dates. A grounded assistant should say "
            "the knowledge base does not contain those answers rather than inventing them.",
        ),
        fact_box("TEST FACT: Prepared data-export download links remain valid for 72 hours.", PALE_GOLD),
    ]
    return story


def main():
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    document = SimpleDocTemplate(
        str(OUTPUT),
        pagesize=letter,
        rightMargin=0.8 * inch,
        leftMargin=0.8 * inch,
        topMargin=0.72 * inch,
        bottomMargin=0.78 * inch,
        title="Northstar Help Center - Customer Guide",
        author="MafPlayground sample",
        subject="Fictional help content for Basic RAG testing",
        creator="scripts/generate-sample-help-pdf.py",
        invariant=1,
        pageCompression=1,
    )
    document.build(build_story(), onFirstPage=draw_page, onLaterPages=draw_page)
    print(OUTPUT)


if __name__ == "__main__":
    main()
